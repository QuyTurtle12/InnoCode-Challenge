using System.Security.Claims;
using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class RoundService : IRoundService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IMcqTestService _mcqTestService;
        private readonly IProblemService _problemService;
        private readonly IContestJudgeService _contestJudgeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfigService _configService;

        public RoundService(IMapper mapper, IUOW unitOfWork, IMcqTestService mcqTestService, IProblemService problemService, IContestJudgeService contestJudgeService, IHttpContextAccessor httpContextAccessor, IConfigService configService)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _mcqTestService = mcqTestService;
            _problemService = problemService;
            _contestJudgeService = contestJudgeService;
            _httpContextAccessor = httpContextAccessor;
            _configService = configService;
        }

        public async Task CreateRoundAsync(Guid contestId, CreateRoundDTO roundDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (roundDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest data cannot be null.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(roundDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest name is required.");
                }

                // Validate date range
                if (roundDTO.Start > roundDTO.End)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
                }

                // Validate against contest dates and other rounds
                await ValidateRoundDatesAsync(contestId, roundDTO.Start, roundDTO.End, null);

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                // Map DTO to Entity
                Round round = _mapper.Map<Round>(roundDTO);

                // Assign contest ID
                round.ContestId = contestId;

                // Set initial status
                round.Status = RoundStatusEnum.Closed.ToString();

                // Insert new round
                await roundRepo.InsertAsync(round);

                // Save changes
                await _unitOfWork.SaveAsync();

                //store time limit in config if provided
                if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
                {
                    await UpsertConfigAsync(
                        configRepo,
                        ConfigKeys.RoundTimeLimitSeconds(round.RoundId),
                        roundDTO.TimeLimitSeconds.Value.ToString()
                    );
                }

                // Handle problem type specific logic
                switch (roundDTO.ProblemType)
                {
                    case ProblemTypeEnum.McqTest:
                        await _mcqTestService.CreateMcqTestAsync(round.RoundId, new CreateMcqTestDTO
                        {
                            Name = roundDTO.McqTestConfig?.Name ?? "Default MCQ Test",
                            Config = roundDTO.McqTestConfig?.Config
                        });
                        break;

                    case ProblemTypeEnum.AutoEvaluation:
                        await _problemService.CreateProblemAsync(round.RoundId, new CreateProblemDTO
                        {
                            Type = ProblemTypeEnum.AutoEvaluation,
                            Description = roundDTO.ProblemConfig?.Description ?? "Default Auto Evaluation Problem",
                            Language = roundDTO.ProblemConfig?.Language ?? "python3",
                            PenaltyRate = roundDTO.ProblemConfig?.PenaltyRate ?? 0
                        });
                        break;

                    case ProblemTypeEnum.Manual:
                        await _problemService.CreateProblemAsync(round.RoundId, new CreateProblemDTO
                        {
                            Type = ProblemTypeEnum.Manual,
                            Description = roundDTO.ProblemConfig?.Description ?? "Default Manual Problem",
                            Language = roundDTO.ProblemConfig?.Language ?? "python3",
                            PenaltyRate = roundDTO.ProblemConfig?.PenaltyRate ?? 0
                        });
                        break;

                    default:
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid problem type.");
                }

                // Save all changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                
                if (ex is ErrorException)
                {
                    throw;
                }
                
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Rounds: {ex.Message}");
            }
        }

        public async Task DeleteRoundAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Find round by id with related entities
                Round? round = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .Include(r => r.McqTest)
                    .FirstOrDefaultAsync(r => r.RoundId == id);

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");
                }

                // Delete related Problem and its child entities using ProblemService
                if (round.Problem != null && !round.Problem.DeletedAt.HasValue)
                {
                    await _problemService.DeleteProblemAsync(round.Problem.ProblemId);
                }

                // Delete related McqTest and its child entities using McqTestService
                if (round.McqTest != null && !round.McqTest.DeletedAt.HasValue)
                {
                    await _mcqTestService.DeleteMcqTestAsync(round.McqTest.TestId);
                }

                // Delete round time limit config
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                string timeLimitKey = ConfigKeys.RoundTimeLimitSeconds(round.RoundId);
                Config? timeLimitConfig = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == timeLimitKey && c.Scope == "contest");

                if (timeLimitConfig != null)
                {
                    timeLimitConfig.DeletedAt = DateTime.UtcNow;
                    await configRepo.UpdateAsync(timeLimitConfig);
                }

                // Delete distribution status config if exists
                await _configService.ResetDistributionStatusAsync(round.RoundId);

                // Delete the round
                round.DeletedAt = DateTime.UtcNow;
                await roundRepo.UpdateAsync(round);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Round: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? roundNameSearch, string? contestNameSearch, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Get all rounds with related entities
                IQueryable<Round> query = roundRepo.Entities
                    .Where(r => !r.DeletedAt.HasValue)
                    .Include(r => r.Contest)
                    .Include(r => r.Problem)
                    .Include(r => r.McqTest);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(r => r.RoundId == idSearch.Value);
                }

                if (contestIdSearch.HasValue)
                {
                    query = query.Where(r => r.ContestId == contestIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(roundNameSearch))
                {
                    query = query.Where(r => r.Name.Contains(roundNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(contestNameSearch))
                {
                    query = query.Where(r => r.Contest.Name.Contains(contestNameSearch));
                }

                if (startDate.HasValue)
                {
                    query = query.Where(r => r.Start >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(r => r.End <= endDate.Value);
                }

                // Change to paginated list to facilitate mapping process
                PaginatedList<Round> resultQuery = await roundRepo.GetPagingAsync(query, pageNumber, pageSize);

                //load time limit configs for these rounds
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                List<Guid> roundIds = resultQuery.Items.Select(r => r.RoundId).ToList();

                List<string> tlKeys = roundIds
                    .Select(ConfigKeys.RoundTimeLimitSeconds)
                    .ToList();

                List<Config> tlConfigs = await configRepo.Entities
                    .Where(c => tlKeys.Contains(c.Key) && c.Scope == "contest" && c.DeletedAt == null)
                    .ToListAsync();

                var tlLookup = tlConfigs.ToLookup(c => c.Key);

                // Map entities to DTOs
                IReadOnlyCollection<GetRoundDTO> result = resultQuery.Items.Select(item =>
                {
                    GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(item);

                    roundDTO.ContestName = item.Contest?.Name ?? "N/A";
                    roundDTO.RoundName = item.Name;

                    // map time limit from config
                    string tlKey = ConfigKeys.RoundTimeLimitSeconds(item.RoundId);
                    Config? tlConfig = tlLookup[tlKey].FirstOrDefault();
                    if (tlConfig != null && int.TryParse(tlConfig.Value, out int secs))
                    {
                        roundDTO.TimeLimitSeconds = secs;
                    }

                    // Map problem information if exists
                    if (item.Problem != null && item.Problem.DeletedAt == null)
                    {
                        roundDTO.ProblemType = item.Problem.Type;
                        roundDTO.Problem = _mapper.Map<GetProblemDTO>(item.Problem);
                    }
                    // Map MCQ test information if exists
                    else if (item.McqTest != null)
                    {
                        roundDTO.ProblemType = ProblemTypeEnum.McqTest.ToString();
                        roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(item.McqTest);
                    }

                    return roundDTO;
                }).ToList();

                // Create new paginated list with DTOs
                return new PaginatedList<GetRoundDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize
                );
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Rounds: {ex.Message}");
            }
        }

        public async Task UpdateRoundAsync(Guid id, UpdateRoundDTO roundDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");
                }

                if (roundDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round data cannot be null.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(roundDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round name is required.");
                }

                // Validate date range
                if (roundDTO.Start > roundDTO.End)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
                }
                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                // Find round by id
                Round? round = await roundRepo
                    .Entities
                    .Where(r => r.RoundId == id)
                    .Include(r => r.McqTest)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync();

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");
                }

                // Validate against contest dates and other rounds (excluding current round)
                await ValidateRoundDatesAsync(round.ContestId, roundDTO.Start, roundDTO.End, round.RoundId);

                // Update round properties
                _mapper.Map(roundDTO, round);

                // Handle problem type specific logic
                switch (roundDTO.ProblemType)
                {
                    case ProblemTypeEnum.McqTest:
                        await _mcqTestService.UpdateMcqTestAsync(round.McqTest!.TestId, roundDTO.McqTestConfig!);
                        break;

                    case ProblemTypeEnum.AutoEvaluation:
                        await _problemService.UpdateProblemAsync(round.Problem!.ProblemId, roundDTO.ProblemConfig!);
                        break;

                    case ProblemTypeEnum.Manual:
                        await _problemService.UpdateProblemAsync(round.Problem!.ProblemId, roundDTO.ProblemConfig!);
                        break;

                    default:
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid problem type.");
                }


                // Update the round
                await roundRepo.UpdateAsync(round);

                // update time limit config
                if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
                {
                    await UpsertConfigAsync(
                        configRepo,
                        ConfigKeys.RoundTimeLimitSeconds(round.RoundId),
                        roundDTO.TimeLimitSeconds.Value.ToString()
                    );
                }

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Round: {ex.Message}");
            }
        }

        private async Task ValidateRoundDatesAsync(Guid contestId, DateTime roundStart, DateTime roundEnd, Guid? excludeRoundId)
        {
            // Get Contest Repository
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

            // Fetch the contest
            Contest? contest = await contestRepo.GetByIdAsync(contestId);

            // Check if contest exists
            if (contest == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
            }

            // Validate round dates are within contest dates
            if (contest.Start.HasValue && roundStart < contest.Start.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round start date cannot be before contest start date ({contest.Start.Value:yyyy-MM-dd HH:mm:ss}).");
            }

            if (contest.End.HasValue && roundEnd > contest.End.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round end date cannot be after contest end date ({contest.End.Value:yyyy-MM-dd HH:mm:ss}).");
            }

            // Get Round Repository
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

            // Get all rounds for this contest (excluding the current round if updating)
            IQueryable<Round> existingRoundsQuery = roundRepo.Entities
                .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue);

            if (excludeRoundId.HasValue)
            {
                existingRoundsQuery = existingRoundsQuery.Where(r => r.RoundId != excludeRoundId.Value);
            }

            List<Round> existingRounds = await existingRoundsQuery.ToListAsync();

            // Check for date conflicts with existing rounds
            foreach (Round existingRound in existingRounds)
            {
                // Check if dates overlap
                if (roundStart <= existingRound.End && roundEnd >= existingRound.Start)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        $"Round dates conflict with existing round '{existingRound.Name}' ({existingRound.Start:yyyy-MM-dd HH:mm:ss} - {existingRound.End:yyyy-MM-dd HH:mm:ss}).");
                }
            }
        }

        public async Task DistributeSubmissionsToJudgesAsync(Guid roundId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate round exists and get contest information
                Round? round = await _unitOfWork.GetRepository<Round>()
                    .Entities
                    .Include(r => r.Contest)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

                if (round == null)
                {
                    throw new ErrorException(
                        StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found"
                    );
                }

                // Check if submissions have already been distributed using config service
                bool alreadyDistributed = await _configService.AreSubmissionsDistributedAsync(roundId);

                if (alreadyDistributed)
                {
                    _unitOfWork.CommitTransaction();
                    return;
                }

                // Check if round has a manual problem
                if (round.Problem == null || round.Problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "This round does not have a manual problem type"
                    );
                }

                // Get all judges for this contest
                IList<JudgeInContestDTO> judges = await _contestJudgeService.GetJudgesByContestAsync(round.ContestId);

                if (judges == null || !judges.Any())
                {
                    throw new ErrorException(
                        StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "No judges available for this contest"
                    );
                }

                // Filter only active judges
                List<JudgeInContestDTO> activeJudges = judges.Where(j => j.Status.ToLower() == "active").ToList();

                if (!activeJudges.Any())
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "No active judges available for submission distribution"
                    );
                }

                // Get pending submissions for this round
                List<Submission> pendingSubmissions = await _unitOfWork.GetRepository<Submission>()
                    .Entities
                    .Include(s => s.Team)
                    .Where(s => s.Problem.RoundId == roundId
                                && !s.DeletedAt.HasValue
                                && s.Status == SubmissionStatusEnum.Pending.ToString()
                                && (string.IsNullOrWhiteSpace(s.JudgedBy)))
                    .OrderBy(s => s.CreatedAt)
                    .ToListAsync();

                if (!pendingSubmissions.Any())
                {
                    // No pending submissions to distribute, but still mark as distributed
                    _unitOfWork.CommitTransaction();
                    return;
                }

                // Distribute submissions equally using round-robin algorithm
                int judgeIndex = 0;
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Assign submissions to judges
                foreach (Submission submission in pendingSubmissions)
                {
                    JudgeInContestDTO assignedJudge = activeJudges[judgeIndex];

                    submission.JudgedBy = assignedJudge.UserId.ToString();

                    submissionRepo.Update(submission);

                    // Move to next judge
                    judgeIndex = (judgeIndex + 1) % activeJudges.Count;
                }

                // Mark submissions as distributed in config service
                await _configService.MarkSubmissionsAsDistributedAsync(roundId);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Rollback on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error distributing submissions: {ex.Message}"
                );
            }
        }

        public async Task<PaginatedList<SubmissionDistributionDTO>> GetManualTypeSubmissionsByRoundId(int pageNumber, int pageSize, Guid roundId, SubmissionStatusEnum? statusFilter)
        {
            // Get user ID from JWT token
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Null User Id");

            // Parse user ID to Guid
            Guid judgeUserId = Guid.Parse(userId);

            // Get judge user details
            User? judgeUser = await _unitOfWork.GetRepository<User>()
                .Entities
                .FirstOrDefaultAsync(u => u.UserId == judgeUserId);

            if (judgeUser == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Judge user not found");
            }

            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            // Query submissions for the specified round and judge
            IQueryable<Submission> query = submissionRepo
                .Entities
                .Where(s => s.Problem.RoundId == roundId
                            && s.DeletedAt == null
                            && s.JudgedBy == judgeUserId.ToString())
                .Include(s => s.Team)
                .Include(s => s.Problem)
                .Include(s => s.SubmittedByStudent)
                    .ThenInclude(st => st.User);

            // Apply status filter if provided
            if (statusFilter.HasValue)
            {
                query = query.Where(s => s.Status == statusFilter.Value.ToString());
            }

            PaginatedList<Submission> resultQuery = await submissionRepo.GetPagingAsync(query, pageNumber, pageSize);

            // Project to DTO and order results
            IReadOnlyCollection<SubmissionDistributionDTO> submissions = resultQuery
                .Items.Select(s => new SubmissionDistributionDTO
                {
                    SubmissionId = s.SubmissionId,
                    TeamId = s.TeamId,
                    TeamName = s.Team.Name,
                    SubmittedByStudentId = s.SubmittedByStudentId,
                    SubmitedByStudentName = s.SubmittedByStudent != null ? s.SubmittedByStudent.User.Fullname : "N/A",
                    JudgeUserId = judgeUserId,
                    JudgeEmail = judgeUser.Email,
                    Status = s.Status ?? string.Empty
                })
                .OrderBy(s => s.Status)
                .ToList();

            return new PaginatedList<SubmissionDistributionDTO>(
                submissions,
                resultQuery.TotalCount,
                resultQuery.PageNumber,
                resultQuery.PageSize
            );
        }
        public async Task<int?> GetRoundTimeLimitSecondsAsync(Guid roundId)
        {
            if (roundId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");

            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            string key = ConfigKeys.RoundTimeLimitSeconds(roundId);

            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.Scope == "contest" && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value, out int secs) ? secs : (int?)null;
        }

        private static async Task UpsertConfigAsync(IGenericRepository<Config> repo, string key, string value)
        {
            Config? existing = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key);

            if (existing == null)
            {
                await repo.InsertAsync(new Config
                {
                    Key = key,
                    Value = value,
                    Scope = "contest",
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                await repo.UpdateAsync(existing);
            }
        }

        public async Task MarkFinishFinishRoundAsync(Guid roundId)
        {
            // Get user ID from JWT token
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Null User Id");

            // Get student ID from user ID
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            Guid studentId = studentRepo.Entities.Where(s => s.UserId.ToString() == userId)
                .Select(s => s.StudentId)
                .FirstOrDefault();

            // Check if student has already finished this round
            bool IsAlreadyFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

            if (IsAlreadyFinishedRound)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot submit. You have already finished this round.");
            }

            // Mark student as finished for this round
            await _configService.MarkFinishedSubmissionAsync(roundId, studentId);
        }
    }
}
