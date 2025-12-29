using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.Mcqs;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
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
        private readonly ICloudinaryService _cloudinaryService;

        private readonly INotificationService _notificationService;   
        private readonly IActivityLogWriter _activityLogWriter;       
        private readonly ILogger<RoundService> _logger;               
        public RoundService(
            IMapper mapper,
            IUOW unitOfWork,
            IMcqTestService mcqTestService,
            IProblemService problemService,
            IContestJudgeService contestJudgeService,
            IHttpContextAccessor httpContextAccessor,
            IConfigService configService,
            ICloudinaryService cloudinaryService,
            INotificationService notificationService,               
            IActivityLogWriter activityLogWriter,                     
            ILogger<RoundService> logger)                             
        {

            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _mcqTestService = mcqTestService;
            _problemService = problemService;
            _contestJudgeService = contestJudgeService;
            _httpContextAccessor = httpContextAccessor;
            _configService = configService;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;               
            _activityLogWriter = activityLogWriter;                   
            _logger = logger;                                         
        }

        public async Task CreateRoundAsync(Guid contestId, CreateRoundDTO roundDTO)
        {
            bool committed = false; 
            Round? createdRound = null; 
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (roundDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round data cannot be null.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(roundDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round name is required.");
                }

                // Validate retake round configuration
                await ValidateRetakeRoundAsync(contestId, roundDTO.MainRoundId, roundDTO.IsRetakeRound, roundDTO.ProblemType);

                // Validate Problem Type
                if (roundDTO.ProblemType == ProblemTypeEnum.Manual || roundDTO.ProblemType == ProblemTypeEnum.AutoEvaluation)
                {
                    // Problem configuration is required for these types
                    if (roundDTO.ProblemConfig == null)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Problem configuration is required for the selected problem type.");
                    }

                    // Validate penalty range
                    if (roundDTO.ProblemConfig.PenaltyRate.HasValue && (roundDTO.ProblemConfig.PenaltyRate.Value < 0 || roundDTO.ProblemConfig.PenaltyRate.Value > 1))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Penalty rate must be between 0 and 1");
                    }
                }
                else if (roundDTO.ProblemType == ProblemTypeEnum.McqTest)
                {
                    // MCQ test configuration is required for this type
                    if (roundDTO.McqTestConfig == null)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "MCQ test configuration is required for the selected problem type.");
                    }

                    if (string.IsNullOrWhiteSpace(roundDTO.McqTestConfig.Name))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "MCQ test name is required.");
                    }
                }

                // Validate rounds
                await ValidateRoundInputAsync(contestId, roundDTO, null);

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Map DTO to Entity    
                Round round = _mapper.Map<Round>(roundDTO);

                // Store times in database
                round.Start = roundDTO.Start;
                round.End = roundDTO.End;

                // Assign contest ID
                round.ContestId = contestId;

                // Set retake round properties
                round.IsRetakeRound = roundDTO.IsRetakeRound;
                round.MainRoundId = roundDTO.MainRoundId;

                DateTime now = DateTime.UtcNow;

                // Set initial status based on current time
                if (now < round.Start) round.Status = RoundStatusEnum.Incoming.ToString();
                else if (now >= round.End) round.Status = RoundStatusEnum.Closed.ToString();
                else round.Status = RoundStatusEnum.Opened.ToString();

                // Insert new round
                await roundRepo.InsertAsync(round);

                // Save changes so that RoundId exists for related Problem creation
                await _unitOfWork.SaveAsync();

                // Store time limit in config
                if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
                {
                    await UpsertConfigAsync(
                        configRepo,
                        ConfigKeys.RoundTimeLimitSeconds(round.RoundId),
                        roundDTO.TimeLimitSeconds.Value.ToString()
                    );
                }

                // Store rank cutoff in config
                if (roundDTO.RankCutoff.HasValue)
                {
                    if (roundDTO.RankCutoff.Value < 0)
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                            "RankCutoff must be >= 0 (0 = disabled).");

                    await UpsertConfigAsync(
                        configRepo,
                        ConfigKeys.RoundRankCutoff(round.RoundId),
                        roundDTO.RankCutoff.Value.ToString()
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

                        // If template file provided, upload it
                        if (roundDTO.ProblemConfig != null && roundDTO.ProblemConfig.TemplateFile != null)
                        {
                            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();

                            // upload new template
                            string uploadedUrl = await _cloudinaryService.UploadFileAsync(roundDTO.ProblemConfig.TemplateFile, "code_template");

                            // load the created problem and set TemplateUrl
                            Problem? createdProblem = await problemRepo.Entities
                                .Where(p => p.RoundId == round.RoundId && p.DeletedAt == null)
                                .FirstOrDefaultAsync();

                            if (createdProblem != null)
                            {
                                createdProblem.TemplateUrl = uploadedUrl;
                                await problemRepo.UpdateAsync(createdProblem);
                            }
                        }

                        break;

                    case ProblemTypeEnum.Manual:
                        await _problemService.CreateProblemAsync(round.RoundId, new CreateProblemDTO
                        {
                            Type = ProblemTypeEnum.Manual,
                            Description = roundDTO.ProblemConfig?.Description ?? "Default Manual Problem",
                            Language = roundDTO.ProblemConfig?.Language ?? "python3",
                            PenaltyRate = roundDTO.ProblemConfig?.PenaltyRate ?? 0
                        });

                        // Optional template for manual problems
                        if (roundDTO.ProblemConfig != null && roundDTO.ProblemConfig.TemplateFile != null)
                        {
                            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                            string uploadedUrl = await _cloudinaryService.UploadFileAsync(roundDTO.ProblemConfig.TemplateFile, "code_template");

                            Problem? createdProblem = await problemRepo.Entities
                                .Where(p => p.RoundId == round.RoundId && p.DeletedAt == null)
                                .FirstOrDefaultAsync();

                            if (createdProblem != null)
                            {
                                createdProblem.TemplateUrl = uploadedUrl;
                                await problemRepo.UpdateAsync(createdProblem);
                            }
                        }
                        break;

                    default:
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid problem type.");
                }

                // Save all changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
                committed = true;
                createdRound = round;

            }
            catch (Exception ex)
            {
                if (!committed) _unitOfWork.RollBack();
                if (ex is ErrorException) throw;
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Rounds: {ex.Message}");
            }

            // Activity log
            var actorId = GetCurrentUserGuidOrThrow();

            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundCreate,       
                TargetTypes.Round,                   
                createdRound!.RoundId.ToString());

            SafeEnqueue(() =>
                BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(createdRound.RoundId)),
                "ScheduleRoundStateTransitionsAsync"); 

        }

        private async Task ValidateRetakeRoundAsync(Guid contestId, Guid? mainRoundId, bool isRetakeRound, ProblemTypeEnum? retakeRoundType = null)
        {
            if (!isRetakeRound)
            {
                return;
            }

            // Retake round must have a main round reference
            if (!mainRoundId.HasValue || mainRoundId.Value == Guid.Empty)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Retake round must reference a valid main round ID."
                );
            }

            // Validate main round exists and belongs to same contest
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? mainRound = await roundRepo.Entities
                .Where(r => r.RoundId == mainRoundId.Value
                    && r.ContestId == contestId
                    && !r.DeletedAt.HasValue)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync();

            if (mainRound == null)
            {
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Main round not found or does not belong to this contest."
                );
            }

            // Main round cannot itself be a retake round
            if (mainRound.IsRetakeRound)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Cannot create a retake round for another retake round."
                );
            }

            // Validate that retake round has the same type as main round
            if (retakeRoundType.HasValue)
            {
                string? mainRoundType = null;

                if (mainRound.McqTest != null && mainRound.McqTest.DeletedAt == null)
                {
                    mainRoundType = ProblemTypeEnum.McqTest.ToString();
                }
                else if (mainRound.Problem != null && mainRound.Problem.DeletedAt == null)
                {
                    mainRoundType = mainRound.Problem.Type;
                }

                if (string.IsNullOrEmpty(mainRoundType))
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Main round does not have a valid problem type."
                    );
                }

                if (!string.Equals(retakeRoundType.ToString(), mainRoundType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Retake round type ({retakeRoundType}) must match main round type ({mainRoundType})."
                    );
                }
            }
        }

        public async Task DeleteRoundAsync(Guid id)
        {
            bool committed = false;
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
                    .Where(r => r.RoundId == id)
                    .Include(r => r.Problem)
                    .Include(r => r.McqTest)
                    .FirstOrDefaultAsync();

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");
                }

                // Delete related Problem and its child entities
                if (round.Problem != null && !round.Problem.DeletedAt.HasValue)
                {
                    await _problemService.DeleteProblemAsync(round.Problem.ProblemId);
                }

                // Delete related McqTest and its child entities
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

                // Delete round rank cutoff config
                string rankCutoffKey = ConfigKeys.RoundRankCutoff(round.RoundId);
                Config? rankCutoffConfig = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == rankCutoffKey && c.Scope == "contest");

                if (rankCutoffConfig != null)
                {
                    rankCutoffConfig.DeletedAt = DateTime.UtcNow;
                    await configRepo.UpdateAsync(rankCutoffConfig);
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
                committed = true;

                var actorId = GetCurrentUserGuidOrThrow();

                await SafeWriteActivityAsync(actorId,
                    ActivityActions.RoundDelete,
                    TargetTypes.Round,
                    round.RoundId.ToString());

                await SafeNotifyContestParticipantsAsync(round.ContestId,
                    NotificationTypes.RoundDeleted,
                    new
                    {
                        contestId = round.ContestId,
                        roundId = round.RoundId,
                        name = round.Name,
                        targetType = TargetTypes.Round,
                        targetId = round.RoundId.ToString(),
                        message = $"Round '{round.Name}' has deleted."
                    });
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                if (!committed) _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Round: {ex.Message}");
            }
        }

        private async Task<bool> HasApprovedRetakeAppealAsync(Guid studentUserId, Guid mainRoundId)
        {
            IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

            bool hasApprovedRetake = await appealRepo.Entities
                .AnyAsync(a => a.OwnerId == studentUserId
                    && a.TargetId == mainRoundId
                    && a.State == AppealStateEnum.Closed.ToString()
                    && a.Decision == AppealDecisionEnum.Approved.ToString()
                    && a.AppealResolution == AppealResolutionEnum.Retake.ToString()
                    && a.DeletedAt == null);

            return hasApprovedRetake;
        }

        public async Task<GetRoundDTO> GetRoundByIdAsync(Guid id, string? openCode)
        {
            try
            {
                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Round ID cannot be empty.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Get the specific round with related entities
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == id && !r.DeletedAt.HasValue)
                    .Include(r => r.Contest)
                    .Include(r => r.Problem)
                    .Include(r => r.McqTest)
                    .Include(r => r.MainRound)
                    .FirstOrDefaultAsync();

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                // Load time limit config for this round
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                string tlKey = ConfigKeys.RoundTimeLimitSeconds(round.RoundId);

                Config? tlConfig = await configRepo.Entities
                    .Where(c => c.Key == tlKey && c.Scope == "contest" && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Map entity to DTO using AutoMapper
                GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(round);

                // Map time limit from config
                if (tlConfig != null && int.TryParse(tlConfig.Value, out int secs))
                {
                    roundDTO.TimeLimitSeconds = secs;
                }

                // Map rank cutoff from config
                string rcKey = ConfigKeys.RoundRankCutoff(round.RoundId);

                Config? rcConfig = await configRepo.Entities
                    .Where(c => c.Key == rcKey && c.Scope == "contest" && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (rcConfig != null && int.TryParse(rcConfig.Value, out int cutoff))
                {
                    roundDTO.RankCutoff = cutoff;
                }
                else
                {
                    roundDTO.RankCutoff = 0;
                }

                // Get user role from HttpContext
                string? userRole = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value;

                // If user is a student, perform additional validations
                if (!string.IsNullOrWhiteSpace(userRole) && userRole == RoleConstants.Student)
                {
                    // Get user ID from JWT token
                    string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                    if (string.IsNullOrWhiteSpace(userId))
                    {
                        throw new ErrorException(StatusCodes.Status401Unauthorized,
                            ResponseCodeConstants.UNAUTHORIZED,
                            "User ID not found.");
                    }

                    // Get student ID associated with this user
                    IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                    Student? student = await studentRepo.Entities
                        .Where(s => s.UserId.ToString() == userId && !s.DeletedAt.HasValue)
                        .FirstOrDefaultAsync();

                    // Check if student exists
                    if (student == null)
                    {
                        throw new ErrorException(StatusCodes.Status404NotFound,
                            ResponseCodeConstants.NOT_FOUND,
                            "Student not found.");
                    }

                    Guid studentId = student.StudentId;
                    Guid studentUserId = student.UserId;

                    // If this is a retake round, validate student has approved retake appeal for main round
                    if (round.IsRetakeRound && round.MainRoundId.HasValue)
                    {
                        bool hasApprovedRetake = await HasApprovedRetakeAppealAsync(studentUserId, round.MainRoundId.Value);

                        if (!hasApprovedRetake)
                        {
                            throw new ErrorException(StatusCodes.Status403Forbidden,
                                ResponseCodeConstants.FORBIDDEN,
                                "You do not have permission to access this retake round. An approved appeal for the main round is required.");
                        }
                    }

                    // Check if student has already finished this round
                    bool hasFinishedRound = await _configService.IsStudentFinishedRoundAsync(id, studentId);

                    if (hasFinishedRound)
                    {
                        throw new ErrorException(StatusCodes.Status403Forbidden,
                            ResponseCodeConstants.FORBIDDEN,
                            "You have already finished this round and cannot access its content anymore.");
                    }

                    // Enforce rank cutoff if applicable
                    await EnforceRoundRankCutoffForStudentAsync(round, studentId);

                    // Check if student has already inputted the open code once
                    bool hasInputtedCode = await _configService.HasStudentInputtedOpenCodeAsync(id, studentId);

                    // If student hasn't inputted code yet, validate the provided code
                    if (!hasInputtedCode)
                    {
                        // Validate open code
                        await ValidateOpenCode(id, openCode);

                        // Mark that student has inputted the code
                        await _configService.MarkStudentOpenCodeInputtedAsync(id, studentId);
                    }
                }

                // Map problem information if exists
                if (round.Problem != null && round.Problem.DeletedAt == null)
                {
                    roundDTO.ProblemType = round.Problem.Type;
                    roundDTO.Problem = _mapper.Map<GetProblemDTO>(round.Problem);
                    roundDTO.Problem.TemplateUrl = round.Problem.TemplateUrl;
                }
                // Map MCQ test information if exists
                else if (round.McqTest != null && round.McqTest.DeletedAt == null)
                {
                    roundDTO.ProblemType = ProblemTypeEnum.McqTest.ToString();
                    roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(round.McqTest);
                }

                return roundDTO;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Round: {ex.Message}");
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
                    .Include(r => r.McqTest)
                    .Include(r => r.MainRound);

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

                // Load time limit configs for these rounds
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
                    roundDTO.Start = item.Start;
                    roundDTO.End = item.End;
                    roundDTO.IsRetakeRound = item.IsRetakeRound;
                    roundDTO.MainRoundId = item.MainRoundId;

                    // Map time limit from config
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
                        roundDTO.Problem.TemplateUrl = item.Problem.TemplateUrl;
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
            bool committed = false;
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

                // Prevent updates while round is in "Opened" status
                if (round.Status == RoundStatusEnum.Opened.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Cannot update round while it is in 'Opened' status.");
                }


                // Validate against contest dates and other rounds (excluding current round)
                await ValidateRoundInputAsync(round.ContestId, roundDTO, round.RoundId);

                // Update round properties
                _mapper.Map(roundDTO, round);

                // Store times in database
                round.Start = roundDTO.Start;
                round.End = roundDTO.End;

                // Handle problem type specific logic
                switch (roundDTO.ProblemType)
                {
                    case ProblemTypeEnum.McqTest:
                        // Update MCQ test config
                        await _mcqTestService.UpdateMcqTestAsync(round.McqTest!.TestId, roundDTO.McqTestConfig!);

                        break;
                    case ProblemTypeEnum.AutoEvaluation:
                        // Update Auto Evaluation Test config
                        await _problemService.UpdateProblemAsync(round.Problem!.ProblemId, roundDTO.ProblemConfig!);

                        // Handle template upload and old-file deletion
                        if (roundDTO.ProblemConfig != null && roundDTO.ProblemConfig.TemplateFile != null)
                        {
                            string? oldUrl = round.Problem.TemplateUrl;

                            // upload new template
                            string uploadedUrl = await _cloudinaryService.UploadFileAsync(roundDTO.ProblemConfig.TemplateFile, "code_template");

                            // set new url on problem
                            round.Problem.TemplateUrl = uploadedUrl;
                            await _unitOfWork.GetRepository<Problem>().UpdateAsync(round.Problem);

                            // attempt to delete old file if exists and is different
                            if (!string.IsNullOrWhiteSpace(oldUrl) && !string.Equals(oldUrl, uploadedUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    string? publicId = CloudinaryHelpers.ExtractCloudinaryPublicId(oldUrl);
                                    if (!string.IsNullOrWhiteSpace(publicId))
                                    {
                                        await _cloudinaryService.DeleteFileAsync(publicId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // log for debugging
                                    Console.WriteLine($"Failed to delete old template file: {ex.Message}");
                                }
                            }
                        }

                        break;
                    case ProblemTypeEnum.Manual:
                        // Update Manual Test config
                        await _problemService.UpdateProblemAsync(round.Problem!.ProblemId, roundDTO.ProblemConfig!);

                        // optional template upload
                        if (roundDTO.ProblemConfig != null && roundDTO.ProblemConfig.TemplateFile != null)
                        {
                            string? oldUrl = round.Problem.TemplateUrl;

                            string uploadedUrl = await _cloudinaryService.UploadFileAsync(roundDTO.ProblemConfig.TemplateFile, "code_template");

                            round.Problem.TemplateUrl = uploadedUrl;
                            await _unitOfWork.GetRepository<Problem>().UpdateAsync(round.Problem);

                            if (!string.IsNullOrWhiteSpace(oldUrl) && !string.Equals(oldUrl, uploadedUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    string? publicId = CloudinaryHelpers.ExtractCloudinaryPublicId(oldUrl);
                                    if (!string.IsNullOrWhiteSpace(publicId))
                                    {
                                        await _cloudinaryService.DeleteFileAsync(publicId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to delete old template file: {ex.Message}");
                                }
                            }
                        }

                        break;
                    default:
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid problem type.");
                }

                // Update the round
                await roundRepo.UpdateAsync(round);

                // Update time limit config
                if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
                {
                    await UpsertConfigAsync(
                        configRepo,
                        ConfigKeys.RoundTimeLimitSeconds(round.RoundId),
                        roundDTO.TimeLimitSeconds.Value.ToString()
                    );
                }

                // Update rank cutoff config
                if (roundDTO.RankCutoff.HasValue)
                {
                    if (roundDTO.RankCutoff.Value < 0)
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                            "RankCutoff must be >= 0 (0 = disabled).");

                    await UpsertConfigAsync(
                        configRepo,
                        ConfigKeys.RoundRankCutoff(round.RoundId),
                        roundDTO.RankCutoff.Value.ToString()
                    );
                }

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
                committed = true;

                // Activity log
                var actorId = GetCurrentUserGuidOrThrow();

                await SafeWriteActivityAsync(actorId,
                    ActivityActions.RoundUpdate,
                    TargetTypes.Round,
                    round.RoundId.ToString());

                // Notification to participants
                await SafeNotifyContestParticipantsAsync(round.ContestId,
                    NotificationTypes.RoundUpdated,
                    new
                    {
                        contestId = round.ContestId,
                        roundId = round.RoundId,
                        name = round.Name,
                        targetType = TargetTypes.Round,
                        targetId = round.RoundId.ToString(),
                        message = $"Round '{round.Name}' has updated."
                    });

                // Schedule background job to handle state transitions
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(round.RoundId)),
                    "ScheduleRoundStateTransitionsAsync");

            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                if (!committed) _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Round: {ex.Message}");
            }
        }

        private async Task ValidateRoundInputAsync(Guid contestId, BaseRoundDTO roundDTO, Guid? excludeRoundId)
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

            // Validate date range
            if (roundDTO.Start > roundDTO.End)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
            }

            // Validate round dates are within contest dates
            if (contest.Start.HasValue && roundDTO.Start < contest.Start.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round start date cannot be before contest start date ({DateTimeHelpers.ToIso8601String(contest.Start.Value)}).");
            }

            if (contest.End.HasValue && roundDTO.End > contest.End.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round end date cannot be after contest end date ({DateTimeHelpers.ToIso8601String(contest.End.Value)}).");
            }

            // Validate time limit seconds
            if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value < 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "Time limit seconds cannot be negative.");
            }

            // Validate time limit does not exceed round duration
            if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
            {
                TimeSpan roundDuration = roundDTO.End - roundDTO.Start;
                if (roundDTO.TimeLimitSeconds.Value > roundDuration.TotalSeconds)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Time limit seconds cannot exceed the total duration of the round.");
                }
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
                if (roundDTO.Start <= existingRound.End && roundDTO.End >= existingRound.Start)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        $"Round dates conflict with existing round '{existingRound.Name}' ({DateTimeHelpers.ToIso8601String(existingRound.Start)} - {DateTimeHelpers.ToIso8601String(existingRound.End)}).");
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
                    // No pending submissions to distribute
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
                    $"Cannot end round. You have already finished this round.");
            }

            // Mark student as finished for this round
            await _configService.MarkFinishedSubmissionAsync(roundId, studentId);
        }

        public async Task<string> GenerateOpenCode(Guid roundId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate round exists
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                // Retrieve open code from config
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                string key = ConfigKeys.RoundOpenCode(roundId);

                Config? config = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);

                // Generate 4-digit random code
                Random random = new Random();
                string openCode = random.Next(1000, 10000).ToString();

                // Create open code in config if not exists, else update it
                if (config == null)
                {
                    await UpsertConfigAsync(configRepo, key, openCode);
                }
                else
                {
                    config.Value = openCode;
                    configRepo.Update(config);
                }

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return openCode;
            }
            catch (Exception ex)
            {
                // Rollback on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error generating open code: {ex.Message}");
            }
        }

        public async Task ValidateOpenCode(Guid roundId, string? openCode)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(openCode))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Open code cannot be empty.");
                }

                // Check if open code exists in config
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                string key = ConfigKeys.RoundOpenCode(roundId);

                Config? config = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);

                if (config == null || config.Value != openCode)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "Invalid open code.");
                }
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error validating open code: {ex.Message}");
            }
        }
        public async Task<string> GetOpenCode(Guid roundId)
        {
            // Validate round exists
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Round not found.");
            }

            // Retrieve open code from config
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            string key = ConfigKeys.RoundOpenCode(roundId);

            Config? config = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);

            // Validate open code exists
            if (config == null || config.Value == null)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Open code not found.");
            }

            return config.Value;
        }
        public async Task<GetRoundDTO> StartRoundNowAsync(Guid roundId)
        {
            bool committed = false;

            // fields to track state
            Guid contestId = Guid.Empty;
            string roundName = string.Empty;
            Guid persistedRoundId = roundId;

            try
            {
                _unitOfWork.BeginTransaction();

                if (roundId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid round ID.");

                Round round = await GetRoundOwnedByCurrentOrganizerAsync(roundId);

                DateTime now = DateTime.UtcNow;

                if (now >= round.End)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Round already ended. Cannot start now.");

                // Round cannot be started before contest start
                if (round.Contest?.Start.HasValue == true && now < round.Contest.Start.Value)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot start round before contest start.");

                if (round.Contest?.End.HasValue == true && now >= round.Contest.End.Value)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot start round because contest already ended.");

                // Time limit must fit inside remaining duration
                //int? tl = await GetRoundTimeLimitSecondsAsync(roundId);
                //if (tl.HasValue && tl.Value > (round.End - now).TotalSeconds)
                //    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "TimeLimitSeconds exceeds remaining round duration.");

                // Prevent overlap with other rounds
                var roundRepo = _unitOfWork.GetRepository<Round>();
                var otherRounds = await roundRepo.Entities
                    .Where(r => r.ContestId == round.ContestId && r.RoundId != roundId && r.DeletedAt == null)
                    .ToListAsync();

                foreach (var other in otherRounds)
                {
                    if (now <= other.End && round.End >= other.Start)
                            throw new ErrorException(StatusCodes.Status409Conflict, "DATE_CONFLICT",
                            $"New round time conflicts with existing round '{other.Name}' ({DateTimeHelpers.ToIso8601String(other.Start)} - {DateTimeHelpers.ToIso8601String(other.End)}).");
                }

                round.Start = now;
                round.Status = RoundStatusEnum.Opened.ToString();

                await roundRepo.UpdateAsync(round);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                committed = true;
                contestId = round.ContestId;
                roundName = round.Name;
                persistedRoundId = round.RoundId;

            }
            catch
            {
                if (!committed) _unitOfWork.RollBack();
                throw;
            }

            // Activity log
            var actorId = GetCurrentUserGuidOrThrow();

                await SafeWriteActivityAsync(actorId,
                    ActivityActions.RoundStartNow,
                    TargetTypes.Round,
                    persistedRoundId.ToString());

                // Notification to participants
                await SafeNotifyContestParticipantsAsync(contestId,
                    NotificationTypes.RoundStarted,
                    new
                    {
                        contestId = contestId,
                        roundId = persistedRoundId,
                        name = roundName,
                        targetType = TargetTypes.Round,
                        targetId = persistedRoundId.ToString(),
                        message = $"Round '{roundName}' has started."
                    });

                // Schedule background job to handle state transitions
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(persistedRoundId)),
                    "ScheduleRoundStateTransitionsAsync");

                return await GetRoundByIdAsync( persistedRoundId, null);

        }
        public async Task<GetRoundDTO> EndRoundNowAsync(Guid roundId)
        {

            // fields to track state
            bool committed = false;
            Guid contestId = Guid.Empty;
            string roundName = string.Empty;
            Guid persistedRoundId = roundId;
            bool isManual = false;

            try
            {
                _unitOfWork.BeginTransaction();

                if (roundId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid round ID.");

                Round round = await GetRoundOwnedByCurrentOrganizerAsync(roundId);

                DateTime now = DateTime.UtcNow;

                if (now <= round.Start)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot end round before it starts.");

                round.End = now;
                round.Status = RoundStatusEnum.Closed.ToString();

                var roundRepo = _unitOfWork.GetRepository<Round>();
                await roundRepo.UpdateAsync(round);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();
                committed = true;

                // cancel any pending open code regeneration jobs
                RecurringJob.RemoveIfExists($"regenerate-open-code-{persistedRoundId}");

                // capture for later use
                contestId = round.ContestId;
                roundName = round.Name;
                persistedRoundId = round.RoundId;
                isManual = (round.Problem != null && round.Problem.Type == ProblemTypeEnum.Manual.ToString());
            }
            catch
            {
                if (!committed) _unitOfWork.RollBack();
                throw;
            }

            // If manual round, distribute pending submissions immediately (idempotent)
            if (isManual)
                try
                {
                    await DistributeSubmissionsToJudgesAsync(persistedRoundId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DistributeSubmissionsToJudgesAsync failed after ending round {RoundId}", persistedRoundId);
                }

            // activity log
            var actorId = GetCurrentUserGuidOrThrow();

            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundEndNow,
                TargetTypes.Round,
                persistedRoundId.ToString());

            // notification to participants
            await SafeNotifyContestParticipantsAsync(contestId,
                NotificationTypes.RoundEnded,
                new
                {
                    contestId = contestId,
                    roundId = persistedRoundId,
                    name = roundName,
                    targetType = TargetTypes.Round,
                    targetId = persistedRoundId.ToString(),
                    message = $"Round '{roundName}' has ended."
                });

            // Schedule background job to handle state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(persistedRoundId)),
                "ScheduleRoundStateTransitionsAsync");

            return await GetRoundByIdAsync(persistedRoundId, null);

        }
        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(id))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            return id;
        }

        private async Task<Round> GetRoundOwnedByCurrentOrganizerAsync(Guid roundId)
        {
            string currentUserId = GetCurrentUserIdOrThrow();

            var roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                .Include(r => r.Contest)
                .Include(r => r.Problem)
                .FirstOrDefaultAsync();

            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            if (round.Contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            if (!string.Equals(round.Contest.CreatedBy, currentUserId, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only the organizer who created this contest can modify rounds.");

            return round;
        }

        // Get current user GUID from JWT token or throw error
        private Guid GetCurrentUserGuidOrThrow()
        {
            string idStr = GetCurrentUserIdOrThrow();
            if (!Guid.TryParse(idStr, out var id) || id == Guid.Empty)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user id.");
            return id;
        }

        // Get all participant user IDs (students and mentors) for a given contest
        private async Task<List<Guid>> GetParticipantUserIdsByContestAsync(Guid contestId)
        {
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var studentIds = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .SelectMany(t => t.TeamMembers.Select(tm => tm.Student.UserId))
                .ToListAsync();

            var mentorIds = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null && t.MentorId != null)
                .Select(t => t.Mentor.UserId)
                .ToListAsync();

            return studentIds.Concat(mentorIds)
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();
        }

        // Write activity log safely, logging any exceptions
        private async Task SafeWriteActivityAsync(Guid actorId, string action, string targetType, string targetId)
        {
            try { await _activityLogWriter.TryWriteAsync(actorId, action, targetType, targetId); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Activity log failed. Action={Action}, Target={TargetType}, TargetId={TargetId}, ActorId={ActorId}",
                    action, targetType, targetId, actorId);
            }
        }

        // Notify contest participants safely, logging any exceptions
        private async Task SafeNotifyContestParticipantsAsync(Guid contestId, string type, object payload)
        {
            try
            {
                var ids = await GetParticipantUserIdsByContestAsync(contestId);
                if (ids.Count == 0) return;
                await _notificationService.CreateInAppToUsersAsync(ids, type, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notify participants failed. ContestId={ContestId}, Type={Type}", contestId, type);
            }
        }

        // Enqueue a Hangfire job safely, logging any exceptions
        private void SafeEnqueue(Action enqueue, string jobName)
        {
            try { enqueue(); }
            catch (Exception ex) { _logger.LogError(ex, "Hangfire enqueue failed: {JobName}", jobName); }
        }

        private async Task<int> GetRoundRankCutoffAsync(Guid roundId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            string key = ConfigKeys.RoundRankCutoff(roundId);

            string? value = await configRepo.Entities
                .AsNoTracking()
                .Where(c => c.Key == key && c.Scope == "contest" && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(value)) return 0;
            return (int.TryParse(value, out int n) && n > 0) ? n : 0;
        }

        private async Task<Round?> FindPreviousMainRoundAsync(Round currentRound)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();

            return await roundRepo.Entities
                .AsNoTracking()
                .Where(r => r.ContestId == currentRound.ContestId
                            && r.RoundId != currentRound.RoundId
                            && r.DeletedAt == null
                            && !r.IsRetakeRound
                            && r.End <= currentRound.Start)
                .OrderByDescending(r => r.End)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync();
        }

        private async Task EnforceRoundRankCutoffForStudentAsync(Round currentRound, Guid studentId)
        {
            if (currentRound.IsRetakeRound) return; // retake handled by appeal logic

            int cutoff = await GetRoundRankCutoffAsync(currentRound.RoundId);
            if (cutoff <= 0) return; // disabled

            Round? prevRound = await FindPreviousMainRoundAsync(currentRound);
            if (prevRound == null) return; // first main round, next

            // Find student's team in this contest
            var teamRepo = _unitOfWork.GetRepository<Team>();
            Guid teamId = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == currentRound.ContestId
                            && t.DeletedAt == null
                            && t.TeamMembers.Any(tm => tm.StudentId == studentId))
                .Select(t => t.TeamId)
                .FirstOrDefaultAsync();

            if (teamId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                    "You are not in a team for this contest.");

            // Top-N teams from previous round (score desc, tie -> earliest submission.CreatedAt)
            List<Guid> topTeamIds = await GetTopTeamsByRoundAsync(currentRound.ContestId, prevRound.RoundId, cutoff);

            if (!topTeamIds.Contains(teamId))
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                    $"Your team is not in Top-{cutoff} of the previous round.");
        }

        private sealed class TeamRankRow
        {
            public Guid TeamId { get; set; }
            public double AvgScore { get; set; }
            public double AvgCreatedAtTicks { get; set; }
        }

        private async Task<List<Guid>> GetTopTeamsByRoundAsync(Guid contestId, Guid prevRoundId, int cutoff)
        {
            if (cutoff <= 0) return new List<Guid>();

            // 1) Load team members in contest
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var memberPairs = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .SelectMany(t => t.TeamMembers
                    .Select(tm => new { t.TeamId, tm.StudentId }))
                .ToListAsync();

            var teamMembers = memberPairs
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.StudentId).Distinct().ToList());

            if (teamMembers.Count == 0) return new List<Guid>();

            // 2) Load all submissions in prev round 
            var submissionRepo = _unitOfWork.GetRepository<Submission>();

            var subs = await submissionRepo.Entities
                .AsNoTracking()
                .Where(s => s.DeletedAt == null
                            && s.Problem != null
                            && s.Problem.RoundId == prevRoundId)
                .Select(s => new
                {
                    s.SubmissionId,
                    s.TeamId,
                    s.SubmittedByStudentId,
                    s.Score,
                    s.Status,
                    s.CreatedAt
                })
                .ToListAsync();

            // Latest submission per (TeamId, StudentId)
            var latestByTeamStudent = subs
                .GroupBy(x => (x.TeamId, x.SubmittedByStudentId))
                .Select(g => g.OrderByDescending(x => x.CreatedAt)
                              .ThenByDescending(x => x.SubmissionId)
                              .First())
                .ToDictionary(x => (x.TeamId, x.SubmittedByStudentId), x => x);

            // 3) Compute avg score + avg createdAt
            var rows = new List<TeamRankRow>(teamMembers.Count);

            foreach (var kv in teamMembers)
            {
                Guid teamId = kv.Key;
                List<Guid> members = kv.Value;
                if (members.Count == 0) continue;

                double sumScore = 0.0;
                double sumTicks = 0.0;

                foreach (var studentId in members)
                {
                    if (latestByTeamStudent.TryGetValue((teamId, studentId), out var last))
                    {
                        bool finished = string.Equals(last.Status, SubmissionStatusEnum.Finished.ToString(), StringComparison.OrdinalIgnoreCase);

                        // Score only when latest is Finished
                        if (finished) sumScore += last.Score;

                        // Tie-break: only use CreatedAt when latest is Finished, else MaxValue
                        sumTicks += finished ? last.CreatedAt.Ticks : DateTime.MaxValue.Ticks;
                    }
                    else
                    {
                        // no submission -> score 0, createdAt loses tie-break
                        sumTicks += DateTime.MaxValue.Ticks;
                    }
                }

                rows.Add(new TeamRankRow
                {
                    TeamId = teamId,
                    AvgScore = sumScore / members.Count,
                    AvgCreatedAtTicks = sumTicks / members.Count
                });
            }

            return rows
                .OrderByDescending(r => r.AvgScore)
                .ThenBy(r => r.AvgCreatedAtTicks) 
                .ThenBy(r => r.TeamId)            
                .Take(cutoff)
                .Select(r => r.TeamId)
                .ToList();
        }

    }
}