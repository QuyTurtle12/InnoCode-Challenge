using System.Security.Claims;
using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.DTOs.SubmissionDetailDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Submissions
{
    public class SubmissionService : ISubmissionService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IJudge0Service _judge0Service;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ILeaderboardEntryService _leaderboardService;

        // Constructor
        public SubmissionService(
            IUOW unitOfWork,
            IMapper mapper,
            IJudge0Service judge0Service,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            ILeaderboardEntryService leaderboardService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _judge0Service = judge0Service;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _leaderboardService = leaderboardService;
        }

        public async Task<PaginatedList<GetSubmissionDTO>> GetPaginatedSubmissionAsync(
            int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch, Guid? SubmittedByStudentId, string? teamName, string? studentName)
        {
            try
            {
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                
                // Start with base query
                IQueryable<Submission> query = submissionRepo
                    .Entities
                    .Where(s => !s.DeletedAt.HasValue)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                    .Include(s => s.SubmissionArtifacts);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(s => s.SubmissionId == idSearch.Value);
                }
                
                if (roundIdSearch.HasValue)
                {
                    query = query.Where(s => s.Problem.RoundId == roundIdSearch.Value);
                }

                if (SubmittedByStudentId.HasValue)
                {
                    query = query.Where(s => s.SubmittedByStudentId == SubmittedByStudentId.Value);
                }

                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    query = query.Where(s => s.SubmittedByStudent!.User.Fullname.Contains(teamName));
                }

                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    query = query.Where(s => s.Team.Name.Contains(teamName));
                }

                // Order by creation date descending
                query = query.OrderByDescending(s => s.CreatedAt);

                // Get paginated data
                PaginatedList<Submission> resultQuery = await submissionRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Fetch all relevant submissions for attempt number calculation
                List<Guid> problemIds = resultQuery.Items.Select(x => x.ProblemId).Distinct().ToList();

                var allRelevantSubmissions = await submissionRepo.Entities
                    .Where(s => problemIds.Contains(s.ProblemId))
                    .Select(s => new { s.SubmissionId, s.ProblemId, s.SubmittedByStudentId, s.CreatedAt })
                    .ToListAsync();

                // Calculate attempt numbers
                var attemptLookup = allRelevantSubmissions
                    .GroupBy(s => new { s.ProblemId, s.SubmittedByStudentId })
                    .SelectMany(g => g.OrderBy(x => x.CreatedAt)
                                      .Select((sub, index) => new { sub.SubmissionId, AttemptNumber = index + 1 }))
                    .ToDictionary(x => x.SubmissionId, x => x.AttemptNumber);

                // Map to DTOs
                IReadOnlyCollection<GetSubmissionDTO> result = resultQuery.Items.Select(item =>
                {
                    // Map basic submission info
                    GetSubmissionDTO? dto = _mapper.Map<GetSubmissionDTO>(item);

                    dto.TeamName = item.Team?.Name ?? string.Empty;

                    dto.SubmittedByStudentName = item.SubmittedByStudent?.User.Fullname!;

                    dto.submissionAttemptNumber = attemptLookup.TryGetValue(item.SubmissionId, out int attemptNum)
                        ? attemptNum
                        : 1;

                    // Map Testcase details to DTOs
                    dto.Details = item.SubmissionDetails?
                        .Select(detail => _mapper.Map<GetSubmissionDetailDTO>(detail))
                        .ToList();

                    // Map Artifacts to DTOs
                    dto.Artifacts = item.SubmissionArtifacts?
                        .Select(artifact => _mapper.Map<GetSubmissionArtifactDTO>(artifact))
                        .ToList();

                    return dto; 
                }).ToList();
                
                // Create new paginated list with mapped DTOs
                return new PaginatedList<GetSubmissionDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated Submissions: {ex.Message}");
            }
        }

        public async Task UpdateSubmissionAsync(Guid id, UpdateSubmissionDTO submissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                
                // Find the existing submission
                Submission? existingSubmission = await submissionRepo.GetByIdAsync(id);
                
                if (existingSubmission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {id} not found.");
                }
                
                // Map DTO to entity
                _mapper.Map(submissionDTO, existingSubmission);
                
                // Update the submission
                await submissionRepo.UpdateAsync(existingSubmission);
                
                // Save changes to the database
                await _unitOfWork.SaveAsync();
                
                // Commit the transaction
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
                    $"Error updating Submission: {ex.Message}");
            }
        }

        public async Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(Guid roundId, CreateSubmissionDTO submissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Check round deadline before allowing submission
                await ValidateRoundDeadlineAsync(roundId, "submit code");

                // Get problem info
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();

                // Find the problem by RoundId
                Problem? problem = await problemRepo
                    .Entities
                    .Where(p => p.RoundId == roundId)
                    .FirstOrDefaultAsync();

                // If no problem found, throw error
                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"The round {roundId} does not have problem");
                }

                // Get test cases for the problem
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                IList<TestCase> testCases = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.TestCase.ToString())
                    .ToList();

                if (!testCases.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"No test cases found for problem {problem.ProblemId}");
                }

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

                // Get contest ID from round ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                Guid contestId = contestRepo.Entities
                    .Where(c => c.Rounds.Any(r => r.RoundId == roundId) && !c.DeletedAt.HasValue)
                    .Select(c => c.ContestId)
                    .FirstOrDefault();

                // Get team ID for the student in this contest
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                Guid teamId = teamRepo.Entities
                    .Where(t => t.TeamMembers.Any(tm => tm.StudentId == studentId) && !t.DeletedAt.HasValue && t.ContestId == contestId)
                    .Select(t => t.TeamId)
                    .FirstOrDefault();

                // Count previous submissions for this problem by this student's team
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                int previousSubmissionsCount = await submissionRepo.Entities
                    .Where(s => s.ProblemId == problem.ProblemId &&
                           s.TeamId == teamId)
                    .CountAsync();

                // Create a submission record
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = teamId,
                    ProblemId = problem.ProblemId,
                    SubmittedByStudentId = studentId,
                    JudgedBy = "system",
                    Status = SubmissionStatusEnum.Pending.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                await submissionRepo.InsertAsync(submission);

                // Save submission artifact (the code)
                IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                SubmissionArtifact artifact = new SubmissionArtifact
                {
                    ArtifactId = Guid.NewGuid(),
                    SubmissionId = submission.SubmissionId,
                    Type = "code",
                    Url = submissionDTO.Code,
                    CreatedAt = DateTime.UtcNow
                };

                await artifactRepo.InsertAsync(artifact);
                await _unitOfWork.SaveAsync();

                // Convert to Judge0 request format
                JudgeSubmissionRequestDTO judge0Request = new JudgeSubmissionRequestDTO
                {
                    LanguageId = SubmissionHelpers.ConvertToJudge0LanguageId(problem.Language),
                    Code = submissionDTO.Code,
                    Problem = new JudgeProblemDTO
                    {
                        Id = problem.ProblemId.ToString(),
                        Title = problem.Type ?? "Unknown"
                    },
                    TestCases = testCases.Select(tc => new JudgeTestCaseDTO
                    {
                        Id = tc.TestCaseId.ToString(),
                        Stdin = tc.Input ?? string.Empty,
                        ExpectedOutput = tc.ExpectedOutput ?? string.Empty
                    }).ToList(),
                    TimeLimitSec = testCases.Max(tc => tc.TimeLimitMs) / 1000.0 ?? 2.0,
                    MemoryLimitKb = testCases.Max(tc => tc.MemoryKb) ?? 128000
                };

                // Auto evaluate submission using Judge0 service
                JudgeSubmissionResultDTO result = await _judge0Service.AutoEvaluateSubmissionAsync(judge0Request);

                // Set submission ID in result
                result.SubmissionId = submission.SubmissionId.ToString();

                // Save results with penalty applied
                await SaveSubmissionResultAsync(submission.SubmissionId, result, previousSubmissionsCount, problem.PenaltyRate);

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return result;
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error evaluating submission: {ex.Message}");
            }
        }

        public async Task SaveSubmissionResultAsync(Guid submissionId, JudgeSubmissionResultDTO result, int previousSubmissionsCount = 0, double? penaltyRate = null)
        {
            try
            {
                // Update submission status
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                Submission? submission = await submissionRepo.GetByIdAsync(submissionId);

                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {submissionId} not found");
                }

                // Calculate score based on test case weights
                double totalWeight = 0;
                double passedWeight = 0;

                // Get test cases
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                IList<TestCase> testCases = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == submission.ProblemId)
                    .ToList();

                // Create submission details for each test case result
                IGenericRepository<SubmissionDetail> submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                foreach (JudgeCaseResultDTO caseResult in result.Cases)
                {
                    // Find the corresponding test case
                    Guid testCaseGuid = Guid.Parse(caseResult.Id);
                    TestCase? testCase = testCases.FirstOrDefault(tc => tc.TestCaseId == testCaseGuid);

                    if (testCase == null) continue;

                    totalWeight += testCase.Weight;
                    if (caseResult.Status == "success")
                    {
                        passedWeight += testCase.Weight;
                    }

                    // Save submission detail
                    SubmissionDetail detail = new SubmissionDetail
                    {
                        DetailsId = Guid.NewGuid(),
                        SubmissionId = submissionId,
                        TestcaseId = testCaseGuid,
                        Weight = testCase.Weight,
                        Note = caseResult.Status,
                        RuntimeMs = SubmissionHelpers.ParseRuntime(caseResult.Time),
                        MemoryKb = caseResult.MemoryKb,
                        CreatedAt = DateTime.UtcNow
                    };

                    await submissionDetailRepo.InsertAsync(detail);
                }

                // Calculate raw score (before penalty)
                double rawScore = totalWeight > 0 ? passedWeight : 0;

                // Apply penalty if applicable
                double finalScore = rawScore;
                if (penaltyRate.HasValue && previousSubmissionsCount > 0)
                {
                    double penaltyPercentage = penaltyRate.Value * previousSubmissionsCount;
                    double penaltyAmount = rawScore * penaltyPercentage; // Percentage value example: 0.1

                    // Ensure score doesn't go below 0
                    finalScore = Math.Max(0, rawScore - penaltyAmount);
                }

                // Update submission
                submission.Status = SubmissionStatusEnum.Finished.ToString();
                submission.Score = Math.Round(finalScore, 2);

                // Update result summary
                result.Summary.rawScore = Math.Round(rawScore, 2);
                result.Summary.penaltyScore = Math.Round(finalScore, 2);

                // Update the submission record
                await submissionRepo.UpdateAsync(submission);

                // Save all changes
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error saving submission results: {ex.Message}");
            }
        }

        public async Task<Guid> CreateFileSubmissionAsync(Guid roundId, IFormFile file)
        {
            try
            {

                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get the round ID from the problem
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                string? roundName = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId)
                    .Select(r => r.Name)
                    .FirstOrDefaultAsync();

                // Validate problem existence
                if (string.IsNullOrWhiteSpace(roundName))
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Round with ID {roundId} not found");
                }

                // Check round deadline before allowing submission
                await ValidateRoundDeadlineAsync(roundId, "submit file");

                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"No file was provided");
                }

                // Validate file type
                List<string> allowedExtensions = new List<string> { ".zip", ".rar" };
                string fileExtension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"File type {fileExtension} is not supported. Allowed types: {string.Join(", ", allowedExtensions)}");
                }

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

                // Get team ID for the student in this round's contest
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                Guid teamId = await teamRepo.Entities
                    .Where(t => t.TeamMembers.Any(tm => tm.StudentId == studentId) &&
                                !t.DeletedAt.HasValue &&
                                t.Contest.Rounds.Any(r => r.RoundId == roundId))
                    .Select(t => t.TeamId)
                    .FirstOrDefaultAsync();

                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Get previous submissions for this problem by this student's team
                List<Submission>? previousSubmissions = await _unitOfWork.GetRepository<Submission>()
                    .Entities
                    .Where(s => s.Problem.Round.RoundId == roundId &&
                                s.TeamId == teamId &&
                                !s.DeletedAt.HasValue)
                    .ToListAsync();

                // Mark previous submissions as deleted
                if (previousSubmissions != null)
                {
                    foreach (Submission item in previousSubmissions)
                    {
                        item.DeletedAt = DateTime.UtcNow;

                        await submissionRepo.UpdateAsync(item);
                    }

                    await _unitOfWork.SaveAsync();
                }

                // Upload file to Cloudinary
                string fileUrl = await _cloudinaryService.UploadFileAsync(file, "submissions");

                // Get problem ID of the round
                Guid problemId = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId)
                    .Select(r => r.Problem!.ProblemId)
                    .FirstOrDefaultAsync();

                // Create a submission record
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = teamId,
                    ProblemId = problemId,
                    SubmittedByStudentId = studentId,
                    JudgedBy = "pending",
                    Status = SubmissionStatusEnum.Pending.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                await submissionRepo.InsertAsync(submission);

                // Save submission artifact (the file URL)
                IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                SubmissionArtifact artifact = new SubmissionArtifact
                {
                    ArtifactId = Guid.NewGuid(),
                    SubmissionId = submission.SubmissionId,
                    Type = "file",
                    Url = fileUrl,
                    CreatedAt = DateTime.UtcNow
                };

                await artifactRepo.InsertAsync(artifact);

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                return submission.SubmissionId;
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating file submission: {ex.Message}");
            }
        }

        public async Task<string> GetFileSubmissionDownloadUrlAsync(Guid submissionId)
        {
            try
            {
                // Get the submission and its artifacts
                IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();

                var fileArtifact = await artifactRepo.Entities
                    .Where(a => a.SubmissionId == submissionId && a.Type == "file")
                    .FirstOrDefaultAsync();

                if (fileArtifact == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No file found for submission {submissionId}");
                }

                return fileArtifact.Url;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving file download URL: {ex.Message}");
            }
        }

        public async Task<bool> UpdateFileSubmissionScoreAsync(Guid submissionId, double score, string feedback)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get submission
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                Submission? submission = await submissionRepo.Entities
                    .Where(s => s.SubmissionId == submissionId)
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .FirstOrDefaultAsync();

                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {submissionId} not found");
                }

                // Get user ID from JWT token (the judge)
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Null User Id");

                // Get judge email
                IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
                User? user = await userRepo.GetByIdAsync(Guid.Parse(userId));
                string judgeEmail = user?.Email ?? "unknown";

                // Update submission
                submission.Score = score;
                submission.Status = SubmissionStatusEnum.Finished.ToString();
                submission.JudgedBy = judgeEmail;

                await submissionRepo.UpdateAsync(submission);

                // Add feedback as a submission detail
                IGenericRepository<SubmissionDetail> detailRepo = _unitOfWork.GetRepository<SubmissionDetail>();
                var detail = new SubmissionDetail
                {
                    DetailsId = Guid.NewGuid(),
                    SubmissionId = submissionId,
                    TestcaseId = null, // No test case for file submissions
                    Weight = score,
                    Note = feedback,
                    RuntimeMs = 0, // Not applicable
                    MemoryKb = 0, // Not applicable
                    CreatedAt = DateTime.UtcNow
                };

                await detailRepo.InsertAsync(detail);
                await _unitOfWork.SaveAsync();

                // Update leaderboard
                Guid contestId = submission.Problem.Round.ContestId;
                try
                {
                    // Update team score in leaderboard
                    await _leaderboardService.AddScoreToTeamAsync(contestId, submission.TeamId, score);
                }
                catch (Exception ex)
                {
                    // Log the error but do not fail the entire operation
                    Console.WriteLine($"Failed to update leaderboard: {ex.Message}");
                }

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return true;
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating file submission score: {ex.Message}");
            }
        }

        public async Task<GetSubmissionDTO> GetSubmissionResultOfLoggedInStudentAsync(Guid roundId)
        {
            try
            {
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

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

                // Find the latest submission for the problem by the logged-in student
                Submission? submission = await submissionRepo.Entities
                   .Where(s => s.Problem.RoundId == roundId &&
                                s.SubmittedByStudentId == studentId)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                    .Include(s => s.SubmissionArtifacts)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                // Get round name for error message
                string roundName = await _unitOfWork.GetRepository<Round>()
                    .Entities
                    .Where(r => r.RoundId == roundId)
                    .Select(r => r.Name)
                    .FirstOrDefaultAsync() ?? "Unknown";

                // Validate submission existence
                if (submission == null) {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No submission found for round \"{roundName}\" by the logged-in student");
                }

                // Map to DTO
                GetSubmissionDTO dto = _mapper.Map<GetSubmissionDTO>(submission);
                dto.TeamName = submission.Team?.Name ?? string.Empty;
                dto.SubmittedByStudentName = submission.SubmittedByStudent?.User.Fullname ?? string.Empty;
                dto.submissionAttemptNumber = await submissionRepo.Entities
                    .Where(s => s.Problem.RoundId == roundId &&
                                s.SubmittedByStudentId == submission.SubmittedByStudentId &&
                                s.CreatedAt <= submission.CreatedAt)
                    .CountAsync();

                // Map Testcase details to DTOs
                if (submission.SubmissionDetails != null)
                {
                    dto.Details = submission.SubmissionDetails
                        .Select(detail => _mapper.Map<GetSubmissionDetailDTO>(detail))
                        .ToList();
                } else
                {
                    dto.Details = null;
                }

                // Map Artifacts to DTOs
                if (submission.SubmissionArtifacts != null)
                {
                    dto.Artifacts = submission.SubmissionArtifacts
                        .Select(artifact => _mapper.Map<Repository.DTOs.SubmissionArtifactDTOs.GetSubmissionArtifactDTO>(artifact))
                        .ToList();
                }
                else
                {
                    dto.Artifacts = null;
                }

                return dto;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating file submission score: {ex.Message}");
            }
        }

        public async Task AddScoreToTeamInLeaderboardAsync(Guid submissionId)
        {
            try
            {
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Find the latest submission for the problem of the logged-in student
                Submission? submission = await submissionRepo.Entities
                    .Where(s => s.SubmissionId == submissionId)
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                // Validate submission existence
                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No submission found for this problem");
                }

                // Get contest ID and score
                Guid contestId = submission.Problem.Round.ContestId;
                double score = submission.Score;

                // Update team score in leaderboard
                await _leaderboardService.AddScoreToTeamAsync(contestId, submission.TeamId, score);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error add score to leaderboard: {ex.Message}");
            }
        }

        private async Task ValidateRoundDeadlineAsync(Guid roundId, string operationName)
        {
            // Get the round with start and end times
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                .FirstOrDefaultAsync();

            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Round with ID {roundId} not found");
            }

            // Get current UTC time
            DateTime now = DateTime.UtcNow;

            // Check if round has started
            if (now < round.Start)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot {operationName}. Round \"{round.Name}\" has not started yet. Start time: {round.Start:yyyy-MM-dd HH:mm:ss} UTC");
            }

            // Check if round has ended
            if (now > round.End)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot {operationName}. Round \"{round.Name}\" has already ended. End time: {round.End:yyyy-MM-dd HH:mm:ss} UTC");
            }
        }

        public async Task<RubricEvaluationResultDTO> SubmitRubricEvaluationAsync(Guid submissionId, SubmitRubricScoreDTO rubricScoreDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();
                IGenericRepository<SubmissionDetail> detailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                // Get submission and verify it exists
                Submission? submission = await submissionRepo.Entities
                    .Include(s => s.Problem)
                    .Where(s => s.SubmissionId == submissionId)
                    .FirstOrDefaultAsync();

                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {submissionId} not found");
                }

                // Verify this is a manual problem
                if (submission.Problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Rubric evaluation is only available for manual problem types");
                }

                // Get all rubric criteria for validation
                List<TestCase> rubricCriteria = await rubricRepo.Entities
                    .Where(tc => tc.ProblemId == submission.ProblemId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString())
                    .ToListAsync();

                if (!rubricCriteria.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No rubric criteria found for this problem");
                }

                Dictionary<Guid, TestCase> rubricsDict = rubricCriteria.ToDictionary(tc => tc.TestCaseId);

                // Get submitted criteria IDs
                HashSet<Guid> submittedCriteriaIds = rubricScoreDTO.CriterionScores
                    .Select(cs => cs.RubricId)
                    .ToHashSet();

                // Get all required criteria IDs
                HashSet<Guid> allRequiredCriteriaIds = rubricCriteria
                    .Select(rc => rc.TestCaseId)
                    .ToHashSet();

                // Find missing criteria
                List<Guid> missingCriteriaIds = allRequiredCriteriaIds
                    .Except(submittedCriteriaIds)
                    .ToList();

                // Check if all criteria have been scored
                if (missingCriteriaIds.Any())
                {
                    // Get missing criteria descriptions for better error message
                    List<string> missingDescriptions = rubricCriteria
                        .Where(rc => missingCriteriaIds.Contains(rc.TestCaseId))
                        .Select(rc => rc.Description ?? "Unnamed criterion")
                        .ToList();

                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"All criteria must be scored. Missing scores for {missingCriteriaIds.Count} criterion/criteria: {string.Join(", ", missingDescriptions)}");
                }

                // Check for duplicate criteria in submission
                if (rubricScoreDTO.CriterionScores.Count != submittedCriteriaIds.Count)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Duplicate criteria found in submission. Each criterion should be scored only once.");
                }

                // Validate all criterion scores
                double totalScore = 0;
                List<RubricCriterionResultDTO> results = new List<RubricCriterionResultDTO>();

                foreach (RubricCriterionScoreDTO criterionScore in rubricScoreDTO.CriterionScores)
                {
                    // Validate rubric exists
                    if (!rubricsDict.TryGetValue(criterionScore.RubricId, out TestCase? criterion))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            $"Rubric {criterionScore.RubricId} not found or does not belong to this problem");
                    }

                    // Validate score doesn't exceed max score
                    if (criterionScore.Score > criterion.Weight)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            $"Score {criterionScore.Score} exceeds max score {criterion.Weight} for criterion: {criterion.Description}");
                    }

                    if (criterionScore.Score < 0)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            $"Score cannot be negative for criterion: {criterion.Description}");
                    }

                    // Check if submission detail already exists for this criterion
                    SubmissionDetail? existingDetail = await detailRepo.Entities
                        .FirstOrDefaultAsync(sd => sd.SubmissionId == submissionId
                            && sd.TestcaseId == criterionScore.RubricId);

                    if (existingDetail != null)
                    {
                        // Update existing detail
                        existingDetail.Weight = criterionScore.Score;
                        existingDetail.Note = criterionScore.Note;
                        await detailRepo.UpdateAsync(existingDetail);
                    }
                    else
                    {
                        // Create new submission detail
                        SubmissionDetail detail = new SubmissionDetail
                        {
                            DetailsId = Guid.NewGuid(),
                            SubmissionId = submissionId,
                            TestcaseId = criterionScore.RubricId,
                            Weight = criterionScore.Score,
                            Note = criterionScore.Note,
                            RuntimeMs = 0,
                            MemoryKb = 0,
                            CreatedAt = DateTime.UtcNow
                        };

                        await detailRepo.InsertAsync(detail);
                    }

                    totalScore += criterionScore.Score;

                    results.Add(new RubricCriterionResultDTO
                    {
                        RubricId = criterionScore.RubricId,
                        Description = criterion.Description ?? criterion.Input,
                        MaxScore = criterion.Weight,
                        Score = criterionScore.Score,
                        Note = criterionScore.Note
                    });
                }

                // Get user ID from JWT token (the judge)
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "User ID not found");

                // Get judge email
                IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
                User? user = await userRepo.GetByIdAsync(Guid.Parse(userId));
                string judgeEmail = user?.Email ?? "unknown";

                // Update submission with total score and status
                submission.Score = Math.Round(totalScore, 2);
                submission.Status = SubmissionStatusEnum.Finished.ToString();
                submission.JudgedBy = judgeEmail;

                // Update submission record
                await submissionRepo.UpdateAsync(submission);

                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return new RubricEvaluationResultDTO
                {
                    SubmissionId = submissionId,
                    JudgedBy = judgeEmail,
                    TotalScore = Math.Round(totalScore, 2),
                    MaxPossibleScore = rubricCriteria.Sum(tc => tc.Weight),
                    CriterionResults = results
                };
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error submitting rubric evaluation: {ex.Message}");
            }
        }

        public async Task<RubricEvaluationResultDTO> GetMyManualTestResultAsync(Guid roundId)
        {
            try
            {
                // Get user ID from JWT token
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "User ID not found");

                // Get student ID from user ID
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                Guid studentId = await studentRepo.Entities
                    .Where(s => s.UserId.ToString() == userId)
                    .Select(s => s.StudentId)
                    .FirstOrDefaultAsync();

                if (studentId == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Student not found");
                }

                // Get the submission for this student in the specified round
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                Submission? submission = await submissionRepo.Entities
                    .Include(s => s.Problem)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .Where(s => s.Problem.RoundId == roundId
                        && s.SubmittedByStudentId == studentId
                        && s.Problem.Type == ProblemTypeEnum.Manual.ToString()
                        && !s.DeletedAt.HasValue)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No manual test submission found for this round");
                }

                // Get all rubric criteria for max scores
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();
                List<TestCase> rubricCriteria = await rubricRepo.Entities
                    .Where(tc => tc.ProblemId == submission.ProblemId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString())
                    .ToListAsync();

                // Map submission details to criterion results
                List<RubricCriterionResultDTO> results = submission.SubmissionDetails
                    .Where(sd => sd.TestcaseId.HasValue)
                    .Select(d => new RubricCriterionResultDTO
                    {
                        RubricId = d.TestcaseId!.Value,
                        Description = d.Testcase?.Description ?? d.Testcase?.Input ?? "Criterion",
                        MaxScore = d.Testcase?.Weight ?? 0,
                        Score = d.Weight ?? 0,
                        Note = d.Note
                    })
                    .ToList();

                // Get judge email
                IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
                string? judgeEmail = await userRepo.Entities
                    .Where(u => u.UserId == Guid.Parse(submission.JudgedBy!))
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync() ?? submission.JudgedBy;

                RubricEvaluationResultDTO result = new RubricEvaluationResultDTO
                {
                    SubmissionId = submission.SubmissionId,
                    StudentName = submission.SubmittedByStudent?.User.Fullname ?? "Unknown",
                    TeamName = submission.Team?.Name ?? "Unknown",
                    SubmittedAt = submission.CreatedAt,
                    JudgedBy = judgeEmail,
                    TotalScore = submission.Score,
                    MaxPossibleScore = rubricCriteria.Sum(tc => tc.Weight),
                    CriterionResults = results
                };

                return result;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving manual test result: {ex.Message}");
            }
        }

        public async Task<PaginatedList<RubricEvaluationResultDTO>> GetAllManualTestResultsByRoundAsync(
            Guid roundId,
            int pageNumber,
            int pageSize,
            Guid? studentIdSearch,
            Guid? teamIdSearch,
            string? studentNameSearch,
            string? teamNameSearch)
        {
            try
            {
                // Validate pagination parameters
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                // Get repositories
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();

                // Build query for manual test submissions in the specified round
                IQueryable<Submission> query = submissionRepo.Entities
                    .Include(s => s.Problem)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .Where(s => s.Problem.RoundId == roundId
                        && s.Problem.Type == ProblemTypeEnum.Manual.ToString()
                        && !s.DeletedAt.HasValue);

                // Apply filters
                if (studentIdSearch.HasValue)
                {
                    query = query.Where(s => s.SubmittedByStudentId == studentIdSearch.Value);
                }

                if (teamIdSearch.HasValue)
                {
                    query = query.Where(s => s.TeamId == teamIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(studentNameSearch))
                {
                    query = query.Where(s => s.SubmittedByStudent!.User!.Fullname.Contains(studentNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(teamNameSearch))
                {
                    query = query.Where(s => s.Team.Name.Contains(teamNameSearch));
                }

                // Order by most recent first
                query = query.OrderByDescending(s => s.CreatedAt);

                // Get paginated submissions
                PaginatedList<Submission> paginatedSubmissions = await submissionRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Get problem IDs to fetch rubric criteria
                List<Guid> problemIds = paginatedSubmissions.Items
                    .Select(s => s.ProblemId)
                    .Distinct()
                    .ToList();

                // Get all rubric criteria for these problems
                Dictionary<Guid, List<TestCase>> rubricsByProblem = await rubricRepo.Entities
                    .Where(tc => problemIds.Contains(tc.ProblemId)
                        && tc.Type == TestCaseTypeEnum.Manual.ToString())
                    .GroupBy(tc => tc.ProblemId)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.ToList());

                // Get unique judge IDs from paginated submissions
                List<string> judgeIds = paginatedSubmissions.Items
                    .Where(s => !string.IsNullOrEmpty(s.JudgedBy))
                    .Select(s => s.JudgedBy!)
                    .Distinct()
                    .ToList();

                // Convert to Guids for query
                List<Guid> judgeGuids = judgeIds
                    .Select(id => Guid.TryParse(id, out Guid guid) ? guid : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToList();

                // Get judge emails directly from User table
                Dictionary<string, string> judgeEmailsLookup = await _unitOfWork.GetRepository<User>()
                    .Entities
                    .Where(u => judgeGuids.Contains(u.UserId))
                    .ToDictionaryAsync(
                        u => u.UserId.ToString(),
                        u => u.Email
                    );

                // Map to DTOs
                IReadOnlyCollection<RubricEvaluationResultDTO> results = paginatedSubmissions.Items.Select(submission =>
                {
                    // Get rubric criteria for this submission's problem
                    List<TestCase> rubricCriteria = rubricsByProblem.GetValueOrDefault(submission.ProblemId) ?? new List<TestCase>();

                    // Map submission details to criterion results
                    List<RubricCriterionResultDTO> criterionResults = submission.SubmissionDetails
                        .Where(sd => sd.TestcaseId.HasValue)
                        .Select(d => new RubricCriterionResultDTO
                        {
                            RubricId = d.TestcaseId!.Value,
                            Description = d.Testcase?.Description ?? d.Testcase?.Input ?? "Criterion",
                            MaxScore = d.Testcase?.Weight ?? 0,
                            Score = d.Weight ?? 0,
                            Note = d.Note
                        })
                        .ToList();

                    // Get judge email from lookup dictionary, fallback to JudgedBy value if not found
                    string judgeEmail = submission.JudgedBy != null && judgeEmailsLookup.TryGetValue(submission.JudgedBy, out string? email)
                        ? email
                        : submission.JudgedBy ?? "Unknown";

                    return new RubricEvaluationResultDTO
                    {
                        SubmissionId = submission.SubmissionId,
                        JudgedBy = judgeEmail,
                        TotalScore = submission.Score,
                        MaxPossibleScore = rubricCriteria.Sum(tc => tc.Weight),
                        CriterionResults = criterionResults,
                        StudentName = submission.SubmittedByStudent?.User?.Fullname ?? "Unknown",
                        TeamName = submission.Team?.Name ?? "Unknown",
                        SubmittedAt = submission.CreatedAt
                    };
                }).ToList();

                return new PaginatedList<RubricEvaluationResultDTO>(
                    results,
                    paginatedSubmissions.TotalCount,
                    paginatedSubmissions.PageNumber,
                    paginatedSubmissions.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving manual test results: {ex.Message}");
            }
        }
    }
}
