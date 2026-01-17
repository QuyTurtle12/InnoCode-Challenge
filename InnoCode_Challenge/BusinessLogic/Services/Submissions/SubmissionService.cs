using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Dashboards;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.PlagiarismDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.DTOs.SubmissionDetailDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using SharpCompress.Archives;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
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
        private readonly IConfigService _configService;
        private readonly IMockTestService _mockTestExecutor;
        private readonly INotificationService _notificationService;
        private readonly IActivityLogWriter _logWriter;
        private readonly ILogger<SubmissionService> _logger;
        private readonly IRoundService _roundService;
        private readonly IContestJudgeService _contestJudgeService;
        private readonly IDashboardNotifierService _dashboardNotifier;

        private const string OPERATION_NAME = "submit code";
        private const string DEFAULT_JUDGED_BY = "system";
        private const double DEFAULT_TIMELIMIT = 10.0;
        private const int DEFAULT_MEMORY = 512000;
        private const string FILE_ARTIFACT_TYPE = "file";
        private const string CODE_ARTIFACT_TYPE = "code";
        private const string AUTO_TEST_SUBMISSION_FOLDER = "code-submissions";
        private const string MANUAL_TEST_SUBMISSION_FOLDER = "submissions";
        private const int DEFAULT_JUDGE_RESCORE_DAYS = 1;
        private const string SCOPE_CONTEST = "contest";
        private const string SCOPE_ROUND = "round";
        private const double DEFAULT_MAX_SCORE = 100.0;

        // Submission status enum values
        private static readonly string SUBMISSION_STATUS_PENDING = SubmissionStatusEnum.Pending.ToString();
        private static readonly string SUBMISSION_STATUS_FINISHED = SubmissionStatusEnum.Finished.ToString();

        // Problem type enum values
        private static readonly string PROBLEM_TYPE_MANUAL = ProblemTypeEnum.Manual.ToString();
        private static readonly string PROBLEM_TYPE_AUTO_EVALUATION = ProblemTypeEnum.AutoEvaluation.ToString();

        // Test case type enum values
        private static readonly string TESTCASE_TYPE_TESTCASE = TestCaseTypeEnum.TestCase.ToString();
        private static readonly string TESTCASE_TYPE_MANUAL = TestCaseTypeEnum.Manual.ToString();

        private const string JUDGE_STATUS_ACTIVE = "active";
        private const string STATUS_PLAGIARISM_SUSPECTED = "PlagiarismSuspected";
        private const string STATUS_PLAGIARISM_CONFIRMED = "PlagiarismConfirmed";
        private const string FP_ALGORITHM = "sha256_py_v1";
        private const int MIN_NORMALIZED_LEN_TO_CHECK = 120;
        private const long MAX_ARCHIVE_BYTES = 25 * 1024 * 1024;
        private const long MAX_TOTAL_PY_BYTES = 2 * 1024 * 1024;
        private const int MAX_PY_FILES = 50;
        private const int MOCK_TEST_EXECUTION_TIME_LIMIT_SECONDS = 20;
        private const int MOCK_TEST_EXECUTION_MEMORY_LIMIT = 2000;

        private const string EXTENSION_PY = ".py";
        private const string EXTENSION_PYTHON = ".python";
        private const string EXTENSION_ZIP = ".zip";
        private const string EXTENSION_RAR = ".rar";

        // ignore these file in archive
        private static readonly string[] IGNORE_PATH_CONTAINS = new[]
        {
            "__pycache__", "/venv/", "\\venv\\", "/.venv/", "\\.venv\\",
            "site-packages", "/dist/", "\\dist\\", "/build/", "\\build\\"
        };

        // Constructor
        public SubmissionService(
            IUOW unitOfWork,
            IMapper mapper,
            IJudge0Service judge0Service,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            ILeaderboardEntryService leaderboardService,
            IConfigService configService,
            IMockTestService mockTestExecutor,
            INotificationService notificationService,
            IActivityLogWriter logWriter,
            IRoundService roundService,
            ILogger<SubmissionService> logger,
            IContestJudgeService contestJudgeService,
            IDashboardNotifierService dashboardNotifier)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _judge0Service = judge0Service;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _leaderboardService = leaderboardService;
            _configService = configService;
            _mockTestExecutor = mockTestExecutor;
            _notificationService = notificationService;
            _logWriter = logWriter;
            _roundService = roundService;
            _logger = logger;
            _contestJudgeService = contestJudgeService;
            _dashboardNotifier = dashboardNotifier;
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

        public async Task<JudgeSubmissionResultDTO> CreateNullAutoSubmissionAsync(Guid roundId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Check round deadline before allowing submission
                await ValidateRoundDeadlineAsync(roundId, OPERATION_NAME);

                // Get problem info
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                Problem? problem = await problemRepo
                    .Entities
                    .Where(p => p.RoundId == roundId)
                    .FirstOrDefaultAsync();

                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"The round {roundId} does not have problem");
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

                bool IsAlreadyFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

                if (IsAlreadyFinishedRound)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        $"Cannot submit. You have already finished this round.");
                }

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

                // Create a submission record with 0 score
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = teamId,
                    ProblemId = problem.ProblemId,
                    SubmittedByStudentId = studentId,
                    JudgedBy = DEFAULT_JUDGED_BY,
                    Status = SubmissionStatusEnum.Finished.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                await submissionRepo.InsertAsync(submission);
                await _unitOfWork.SaveAsync();

                // Get test cases for result structure
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                IList<TestCase> testCases = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.TestCase.ToString())
                    .ToList();

                // Create submission details with all test cases failed
                IGenericRepository<SubmissionDetail> submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                foreach (TestCase testCase in testCases)
                {
                    SubmissionDetail detail = new SubmissionDetail
                    {
                        DetailsId = Guid.NewGuid(),
                        SubmissionId = submission.SubmissionId,
                        TestcaseId = testCase.TestCaseId,
                        Weight = testCase.Weight,
                        Note = "No submission provided",
                        RuntimeMs = 0,
                        MemoryKb = 0,
                        CreatedAt = DateTime.UtcNow
                    };

                    await submissionDetailRepo.InsertAsync(detail);
                }

                await _unitOfWork.SaveAsync();

                // Create result DTO
                JudgeSubmissionResultDTO result = new JudgeSubmissionResultDTO
                {
                    SubmissionId = submission.SubmissionId.ToString(),
                    Summary = new JudgeSummaryDTO
                    {
                        Total = testCases.Count,
                        Passed = 0,
                        Failed = testCases.Count,
                        rawScore = 0,
                        penaltyScore = 0
                    },
                    Cases = testCases.Select(tc => new JudgeCaseResultDTO
                    {
                        Id = tc.TestCaseId.ToString(),
                        Status = Judge0StatusEnum.Error.ToString(),
                        Time = "0.000",
                        MemoryKb = 0,
                        CompileOutput = null,
                        Stderr = "No submission provided"
                    }).ToList()
                };

                // Mark as finished for the round
                await _configService.MarkFinishedSubmissionAsync(roundId, studentId);

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
                    $"Error creating null auto submission: {ex.Message}");
            }
        }

        public async Task<Guid> CreateNullManualSubmissionAsync(Guid roundId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Check round deadline before allowing submission
                await ValidateRoundDeadlineAsync(roundId, "submit file");

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

                // Get team ID for the student in this round's contest
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                Guid teamId = await teamRepo.Entities
                    .Where(t => t.TeamMembers.Any(tm => tm.StudentId == studentId) &&
                                !t.DeletedAt.HasValue &&
                                t.Contest.Rounds.Any(r => r.RoundId == roundId))
                    .Select(t => t.TeamId)
                    .FirstOrDefaultAsync();

                // Get problem ID of the round
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Guid problemId = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId)
                    .Select(r => r.Problem!.ProblemId)
                    .FirstOrDefaultAsync();

                // Create a submission record with 0 score
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = teamId,
                    ProblemId = problemId,
                    SubmittedByStudentId = studentId,
                    JudgedBy = null,
                    Status = SubmissionStatusEnum.Finished.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                await submissionRepo.InsertAsync(submission);
                await _unitOfWork.SaveAsync();

                // Mark as finished for the round
                await _configService.MarkFinishedSubmissionAsync(roundId, studentId);

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
                    $"Error creating null manual submission: {ex.Message}");
            }
        }

        public async Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(
            Guid roundId,
            CreateSubmissionDTO submissionDTO,
            TestCaseEvaluationTypeEnum evaluationType)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Validate round deadline
                await ValidateRoundDeadlineAsync(roundId, OPERATION_NAME);

                // Get and validate problem
                Problem problem = await GetProblemForRoundAsync(roundId);

                // Get and validate test cases
                IList<TestCase> testCases = await GetTestCasesForProblemAsync(problem.ProblemId);

                // Get student and team information
                var (studentId, teamId, contestId) = await GetStudentAndTeamInfoAsync(roundId);

                // Validate student hasn't finished round
                await ValidateStudentNotFinishedRoundAsync(roundId, studentId);

                // Ensure team is eligible for this round
                await EnsureTeamEligibleForRoundAsync(roundId, teamId);

                // Count previous submissions
                int previousSubmissionsCount = await CountPreviousSubmissionsAsync(problem.ProblemId, studentId);

                // Process submission artifact (file or code)
                var (sourceCode, artifactType, artifactUrl) = await ProcessSubmissionArtifactAsync(
                    submissionDTO, evaluationType, studentId);

                // Create submission record
                Submission submission = await CreateSubmissionRecordAsync(
                    teamId, problem.ProblemId, studentId, artifactType, artifactUrl);

                // Log activity
                await LogSubmissionCreationAsync(submission.SubmissionId);

                // Evaluate submission using Judge0
                JudgeSubmissionResultDTO result = await EvaluateWithJudge0Async(
                    problem, testCases, sourceCode, submission.SubmissionId);

                // Save results with penalty
                await SaveSubmissionResultAsync(
                    submission.SubmissionId, result, previousSubmissionsCount, problem.PenaltyRate);

                // Check for plagiarism
                await CheckAndFlagPlagiarismAsync(submission, problem, sourceCode);

                _unitOfWork.CommitTransaction();

                return result;
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();
                if (ex is ErrorException) throw;
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error evaluating submission: {ex.Message}");
            }
        }


        private async Task<string> UploadCodeAsFileAsync(string code, string fileName)
        {
            try
            {
                // Unescape the code string (convert \n to actual newlines, \t to tabs, etc.)
                string unescapedCode = System.Text.RegularExpressions.Regex.Unescape(code);

                // Create a temporary file
                string tempFilePath = Path.Combine(Path.GetTempPath(), fileName);

                // Write code to temporary file
                await File.WriteAllTextAsync(tempFilePath, unescapedCode, System.Text.Encoding.UTF8);

                try
                {
                    // Open file stream
                    using FileStream fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read);

                    // Create FormFile from stream
                    IFormFile formFile = new FormFile(
                        baseStream: fileStream,
                        baseStreamOffset: 0,
                        length: fileStream.Length,
                        name: "code",
                        fileName: fileName)
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = "text/plain"
                    };

                    // Upload to Cloudinary
                    string cloudinaryUrl = await _cloudinaryService.UploadFileAsync(formFile, AUTO_TEST_SUBMISSION_FOLDER);

                    return cloudinaryUrl;
                }
                finally
                {
                    // Clean up temporary file
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Failed to upload code as file: {ex.Message}");
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

                await TryNotifySubmissionResultAsync(submission);
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
                _unitOfWork.BeginTransaction();

                // Validate round and file
                await ValidateRoundDeadlineAsync(roundId, "submit file");
                ValidateSubmissionFile(file);

                // Get student information
                Guid studentId = await GetCurrentStudentIdAsync();

                // Validate student hasn't finished
                await ValidateStudentNotFinishedRoundAsync(roundId, studentId);

                // Get team ID
                Guid teamId = await GetTeamIdForStudentInRoundAsync(studentId, roundId);

                // Validate team existence
                if (teamId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "You are not in a team for this contest.");

                // Ensure team is eligible
                await EnsureTeamEligibleForRoundAsync(roundId, teamId);

                // Delete previous submissions
                await DeletePreviousSubmissionsAsync(roundId, teamId);

                // Upload file
                string fileUrl = await _cloudinaryService.UploadFileAsync(file, MANUAL_TEST_SUBMISSION_FOLDER);

                // Get problem ID
                Guid problemId = await GetProblemIdForRoundAsync(roundId);

                // Create submission
                Submission submission = await CreateFileSubmissionRecordAsync(
                    teamId, problemId, studentId, fileUrl);

                // Check plagiarism for archives
                await CheckPlagiarismForArchiveAsync(submission, file);

                _unitOfWork.CommitTransaction();

                return submission.SubmissionId;
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();
                if (ex is ErrorException) throw;
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
                if (submission == null)
                {
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
                }
                else
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

        public async Task AcceptResultAsync(Guid submissionId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Find the submission
                Submission? submission = await submissionRepo.Entities
                    .Where(s => s.SubmissionId == submissionId && s.DeletedAt == null)
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .FirstOrDefaultAsync();

                // Validate submission existence
                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {submissionId} not found");
                }

                // Check if submission is flagged for plagiarism
                if (string.Equals(submission.Status, STATUS_PLAGIARISM_SUSPECTED, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ErrorException(StatusCodes.Status409Conflict,
                        ResponseCodeConstants.BADREQUEST,
                        "Submission is flagged for plagiarism and must be reviewed by staff before acceptance.");
                }

                // Get roundId and studentId
                Guid roundId = submission.Problem.RoundId;
                Guid studentId = submission.SubmittedByStudentId;

                // Check if student has already finished this round
                bool IsAlreadyFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

                if (IsAlreadyFinishedRound)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        $"Cannot accept result. You have already finished this round.");
                }

                // Get all other submissions for this student in the same round
                List<Submission> otherSubmissions = await submissionRepo.Entities
                    .Where(s => s.Problem.RoundId == roundId
                        && s.SubmittedByStudentId == studentId
                        && s.SubmissionId != submissionId
                        && s.DeletedAt == null
                        && s.Status != SubmissionStatusEnum.Finished.ToString())
                    .ToListAsync();

                // Set the current submission to Finished
                submission.Status = SubmissionStatusEnum.Finished.ToString();
                await submissionRepo.UpdateAsync(submission);

                // Cancel all other submissions
                foreach (Submission otherSubmission in otherSubmissions)
                {
                    otherSubmission.Status = SubmissionStatusEnum.Cancelled.ToString();
                    await submissionRepo.UpdateAsync(otherSubmission);
                }

                // Save changes
                await _unitOfWork.SaveAsync();

                // Get contest ID for leaderboard update
                Guid contestId = submission.Problem.Round.ContestId;

                // Mark round as finished for this student
                await _configService.MarkFinishedSubmissionAsync(roundId, studentId);

                // Update team score in leaderboard
                await _leaderboardService.UpdateTeamScoreAsync(contestId, submission.TeamId);

                // Commit transaction
                _unitOfWork.CommitTransaction();
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
                    $"Error accepting submission result: {ex.Message}");
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

        public async Task<RubricEvaluationResultDTO> SubmitRubricEvaluationAsync(
            Guid submissionId,
            SubmitRubricScoreDTO rubricScoreDTO)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Get and validate submission
                Submission submission = await GetSubmissionForRubricEvaluationAsync(submissionId);
                string judgeUserId = GetCurrentUserIdOrThrow();
                await EnsureJudgeDeadlineAsync(submission, judgeUserId);

                // Get rubric criteria
                List<TestCase> rubricCriteria = await GetRubricCriteriaAsync(submission.ProblemId);

                // Validate all criteria are scored
                ValidateAllCriteriaScored(rubricCriteria, rubricScoreDTO.CriterionScores);

                // Process and save criterion scores
                var (totalScore, results) = await ProcessCriterionScoresAsync(
                    submissionId, rubricScoreDTO.CriterionScores, rubricCriteria);

                // Get judge information
                string judgeEmail = await GetCurrentJudgeEmailAsync();

                // Update submission with results
                await UpdateSubmissionWithRubricScoreAsync(submission, totalScore);

                // Update leaderboard
                await UpdateLeaderboardAfterRubricEvaluationAsync(submission);

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
                _unitOfWork.RollBack();
                if (ex is ErrorException) throw;
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
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.Team)
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
                        MaxScore = Math.Round(d.Testcase?.Weight ?? 0, 2),
                        Score = Math.Round(d.Weight ?? 0, 2),
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
                    TotalScore = Math.Round(submission.Score, 2),
                    MaxPossibleScore = Math.Round(rubricCriteria.Sum(tc => tc.Weight), 2),
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

                // Get round to validate existence
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                // Validate round existence
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Round ID {roundId} not found");
                }

                // Get repositories
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();

                // Build query for manual test submissions in the specified round
                IQueryable<Submission> query = submissionRepo.Entities
                    .Include(s => s.Problem)
                    .Where(s => s.Problem.RoundId == roundId
                        && s.Problem.Type == ProblemTypeEnum.Manual.ToString()
                        && !s.DeletedAt.HasValue)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase);

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
                            MaxScore = Math.Round(d.Testcase?.Weight ?? 0, 2),
                            Score = Math.Round(d.Weight ?? 0, 2),
                            Note = d.Note
                        })
                        .ToList();

                    // Get judge email from lookup dictionary, fallback to JudgedBy value if not found
                    string judgeEmail = submission.JudgedBy != null && judgeEmailsLookup.TryGetValue(submission.JudgedBy.ToLower(), out string? email)
                        ? email
                        : submission.JudgedBy ?? "Unknown";

                    return new RubricEvaluationResultDTO
                    {
                        SubmissionId = submission.SubmissionId,
                        JudgedBy = judgeEmail,
                        TotalScore = Math.Round(submission.Score, 2),
                        MaxPossibleScore = Math.Round(rubricCriteria.Sum(tc => tc.Weight), 2),
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

        public async Task<GetSubmissionDTO> GetMyAutoTestResultAsync(Guid roundId)
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

                // Get round to validate existence
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                // Validate round existence
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Round ID {roundId} not found");
                }

                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                Submission? submission = await submissionRepo.Entities
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .Where(s => s.Problem.RoundId == roundId
                        && s.SubmittedByStudentId == studentId
                        && s.Problem.Type == PROBLEM_TYPE_AUTO_EVALUATION
                        && !s.DeletedAt.HasValue)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .Include(s => s.SubmissionArtifacts)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                // Calculate attempt number with a separate query
                int attemptNumber = await submissionRepo.Entities
                    .Where(s => s.Problem.RoundId == roundId
                        && s.SubmittedByStudentId == studentId
                        && s.Problem.Type == PROBLEM_TYPE_AUTO_EVALUATION
                        && !s.DeletedAt.HasValue)
                    .CountAsync();

                // Map to DTO
                GetSubmissionDTO dto = _mapper.Map<GetSubmissionDTO>(submission);
                dto.TeamName = submission?.Team?.Name ?? string.Empty;
                dto.SubmittedByStudentName = submission?.SubmittedByStudent?.User.Fullname ?? string.Empty;
                dto.submissionAttemptNumber = attemptNumber;

                // Map test case details to DTOs
                if (submission?.SubmissionDetails != null)
                {
                    dto.Details = submission.SubmissionDetails
                        .Select(detail => new GetSubmissionDetailDTO
                        {
                            DetailsId = detail.DetailsId,
                            CreatedAt = detail.CreatedAt,
                            SubmissionId = detail.SubmissionId,
                            TestcaseId = detail.TestcaseId ?? Guid.Empty,
                            Weight = Math.Round(detail.Weight ?? 0, 2),
                            Note = detail.Note,
                            RuntimeMs = detail.RuntimeMs,
                            MemoryKb = detail.MemoryKb
                        })
                        .ToList();
                }
                else
                {
                    dto.Details = null;
                }

                // Map Artifacts to DTOs
                if (submission?.SubmissionArtifacts != null)
                {
                    dto.Artifacts = submission.SubmissionArtifacts
                        .Select(artifact => _mapper.Map<GetSubmissionArtifactDTO>(artifact))
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
                    $"Error retrieving auto test result: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetSubmissionDTO>> GetAllAutoTestResultsByRoundAsync(
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

                // Build the base query
                IQueryable<Submission> baseQuery = submissionRepo.Entities
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .Where(s => s.Problem.RoundId == roundId
                        && s.Problem.Type == PROBLEM_TYPE_AUTO_EVALUATION
                        && !s.DeletedAt.HasValue)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .Include(s => s.SubmissionArtifacts);

                // Apply filters using
                if (studentIdSearch.HasValue)
                {
                    baseQuery = baseQuery.Where(s => s.SubmittedByStudentId == studentIdSearch.Value);
                }

                if (teamIdSearch.HasValue)
                {
                    baseQuery = baseQuery.Where(s => s.TeamId == teamIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(studentNameSearch))
                {
                    baseQuery = baseQuery.Where(s => s.SubmittedByStudent!.User!.Fullname.Contains(studentNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(teamNameSearch))
                {
                    baseQuery = baseQuery.Where(s => s.Team.Name.Contains(teamNameSearch));
                }

                List<Submission> allSubmissions = await baseQuery.ToListAsync();

                // Group by student and get the latest submission for each
                List<Submission> latestSubmissions = allSubmissions
                    .GroupBy(s => s.SubmittedByStudentId)
                    .Select(g => g.OrderByDescending(s => s.CreatedAt).First())
                    .OrderByDescending(s => s.CreatedAt)
                    .ToList();

                int totalCount = latestSubmissions.Count;
                List<Submission> paginatedSubmissions = latestSubmissions
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Calculate attempt numbers
                List<Guid> studentIds = paginatedSubmissions.Select(s => s.SubmittedByStudentId).Distinct().ToList();

                Dictionary<Guid, int> attemptCounts = allSubmissions
                    .Where(s => studentIds.Contains(s.SubmittedByStudentId))
                    .GroupBy(s => s.SubmittedByStudentId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count()
                    );

                // Map to DTOs
                IReadOnlyCollection<GetSubmissionDTO> results = paginatedSubmissions.Select(submission =>
                {
                    // Map basic submission info
                    GetSubmissionDTO dto = _mapper.Map<GetSubmissionDTO>(submission);
                    dto.TeamName = submission.Team?.Name ?? string.Empty;
                    dto.SubmittedByStudentName = submission.SubmittedByStudent?.User?.Fullname ?? string.Empty;

                    // Set attempt number
                    dto.submissionAttemptNumber = attemptCounts.TryGetValue(submission.SubmittedByStudentId, out int count)
                        ? count
                        : 1;

                    // Map test case details to DTOs
                    if (submission?.SubmissionDetails != null)
                    {
                        dto.Details = submission.SubmissionDetails
                            .Select(detail => new GetSubmissionDetailDTO
                            {
                                DetailsId = detail.DetailsId,
                                CreatedAt = detail.CreatedAt,
                                SubmissionId = detail.SubmissionId,
                                TestcaseId = detail.TestcaseId ?? Guid.Empty,
                                Weight = Math.Round(detail.Weight ?? 0, 2),
                                Note = detail.Note,
                                RuntimeMs = detail.RuntimeMs,
                                MemoryKb = detail.MemoryKb
                            })
                            .ToList();
                    }
                    else
                    {
                        dto.Details = null;
                    }

                    // Map Artifacts to DTOs
                    dto.Artifacts = submission.SubmissionArtifacts?
                        .Select(artifact => _mapper.Map<GetSubmissionArtifactDTO>(artifact))
                        .ToList();

                    return dto;
                }).ToList();

                return new PaginatedList<GetSubmissionDTO>(
                    results,
                    totalCount,
                    pageNumber,
                    pageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving auto test results: {ex.Message}");
            }
        }

        public async Task<PaginatedList<SubmissionDistributionDTO>> GetSubmissionsByJudgeByAsync(
            int pageNumber,
            int pageSize,
            Guid? contestIdSearch,
            string? contestName,
            Guid? roundIdSearch,
            string? roundName,
            Guid? teamIdSearch,
            string? teamName,
            Guid? studentIdSearch,
            string? studentName,
            SubmissionStatusEnum? statusFilter = null)
        {
            try
            {
                // Validate pagination
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Get judge ID
                string judgeId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "User ID not found");

                // Build base query with necessary includes
                IQueryable<Submission> query = submissionRepo.Entities
                    .Where(s => s.JudgedBy != null &&
                                s.JudgedBy.ToLower() == judgeId &&
                                s.DeletedAt == null)
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                            .ThenInclude(r => r.Contest)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase);

                // Apply filters
                if (contestIdSearch.HasValue)
                {
                    query = query.Where(s => s.Problem != null
                                             && s.Problem.Round != null
                                             && s.Problem.Round.ContestId == contestIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(contestName))
                {
                    string formattedContestName = contestName.Trim();
                    query = query.Where(s => s.Problem != null
                                             && s.Problem.Round != null
                                             && s.Problem.Round.Contest != null
                                             && s.Problem.Round.Contest.Name.Contains(formattedContestName));
                }

                if (roundIdSearch.HasValue)
                {
                    query = query.Where(s => s.Problem != null && s.Problem.RoundId == roundIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(roundName))
                {
                    string formattedRoundName = roundName.Trim();
                    query = query.Where(s => s.Problem != null
                                             && s.Problem.Round != null
                                             && s.Problem.Round.Name.Contains(formattedRoundName));
                }

                if (teamIdSearch.HasValue)
                {
                    query = query.Where(s => s.TeamId == teamIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    string formattedTeamName = teamName.Trim();
                    query = query.Where(s => s.Team != null && s.Team.Name.Contains(formattedTeamName));
                }

                if (studentIdSearch.HasValue)
                {
                    query = query.Where(s => s.SubmittedByStudentId == studentIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(studentName))
                {
                    string formattedStudentName = studentName.Trim();
                    query = query.Where(s => s.SubmittedByStudent != null
                                        && s.SubmittedByStudent.User != null
                                        && s.SubmittedByStudent.User.Fullname.Contains(formattedStudentName));
                }

                if (statusFilter.HasValue)
                {
                    string statusString = statusFilter.Value.ToString();
                    query = query.Where(s => s.Status == statusString);
                }

                // Order newest first
                query = query.OrderByDescending(s => s.CreatedAt);

                // Get paginated submissions
                PaginatedList<Submission> paged = await submissionRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Get judge email
                string judgeEmail = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

                // Convert judgeId to Guid
                Guid judgeGuid = Guid.Parse(judgeId);

                // Map to DTOs
                List<SubmissionDistributionDTO> items = paged.Items.Select(s =>
                {
                    // Check if all submission details have associated test cases
                    if (s.SubmissionDetails != null && s.SubmissionDetails.Any(sd => sd.Testcase == null))
                    {
                        throw new ErrorException(StatusCodes.Status404NotFound,
                            ResponseCodeConstants.NOT_FOUND,
                            $"Some submission details are missing associated test cases for submission ID {s.SubmissionId}");
                    }

                    SubmissionDistributionDTO dto = new SubmissionDistributionDTO
                    {
                        SubmissionId = s.SubmissionId,
                        ContestId = s.Problem?.Round?.ContestId ?? Guid.Empty,
                        ContestName = s.Problem?.Round?.Contest?.Name ?? string.Empty,
                        RoundId = s.Problem?.RoundId ?? Guid.Empty,
                        RoundName = s.Problem?.Round?.Name ?? string.Empty,
                        TeamId = s.TeamId,
                        TeamName = s.Team?.Name ?? string.Empty,
                        SubmittedByStudentId = s.SubmittedByStudentId,
                        SubmitedByStudentName = s.SubmittedByStudent?.User?.Fullname ?? string.Empty,
                        JudgeUserId = judgeGuid,
                        JudgeEmail = judgeEmail,
                        Status = s.Status ?? string.Empty,
                        CriterionResults = s.SubmissionDetails?
                            .Where(sd => sd.TestcaseId.HasValue && sd.Testcase != null && !sd.Testcase.DeletedAt.HasValue)
                            .Select(sd => new RubricCriterionResultDTO
                            {
                                RubricId = sd.TestcaseId!.Value,
                                Description = sd.Testcase?.Description ?? sd.Testcase?.Input ?? "Criterion",
                                MaxScore = Math.Round(sd.Testcase?.Weight ?? 0, 2),
                                Score = Math.Round(sd.Weight ?? 0, 2),
                                Note = sd.Note
                            })
                            .ToList() ?? new List<RubricCriterionResultDTO>()
                    };

                    return dto;
                }).ToList();

                return new PaginatedList<SubmissionDistributionDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving submissions distribution: {ex.Message}");
            }
        }

        public async Task<SubmissionDistributionDTO> GetSubmissionByIdAsync(Guid submissionId)
        {
            try
            {
                // Get submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Get submission
                Submission? submission = await submissionRepo.Entities
                    .Where(x => x.SubmissionId == submissionId && x.DeletedAt == null)
                    .Include(x => x.Problem)
                        .ThenInclude(p => p.Round)
                            .ThenInclude(r => r.Contest)
                    .Include(x => x.Team)
                    .Include(x => x.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(x => x.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .FirstOrDefaultAsync();

                // Check if submission exists
                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {submissionId} not found");
                }

                // Check if all submission details have associated test cases
                if (submission.SubmissionDetails != null && submission.SubmissionDetails.Any(sd => sd.Testcase == null))
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Some submission details are missing associated test cases for submission ID {submissionId}");
                }

                // Get judge email and judge id
                string judgeEmail = string.Empty;
                Guid judgeUserId = Guid.Empty;
                if (!string.IsNullOrWhiteSpace(submission.JudgedBy) && Guid.TryParse(submission.JudgedBy, out Guid parsedJudgeId))
                {
                    judgeUserId = parsedJudgeId;
                    IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
                    judgeEmail = await userRepo.Entities
                        .Where(u => u.UserId == parsedJudgeId)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync() ?? string.Empty;
                }

                // Map to DTO
                SubmissionDistributionDTO dto = new SubmissionDistributionDTO
                {
                    SubmissionId = submission.SubmissionId,
                    ContestId = submission.Problem?.Round?.ContestId ?? Guid.Empty,
                    ContestName = submission.Problem?.Round?.Contest?.Name ?? string.Empty,
                    RoundId = submission.Problem?.RoundId ?? Guid.Empty,
                    RoundName = submission.Problem?.Round?.Name ?? string.Empty,
                    TeamId = submission.TeamId,
                    TeamName = submission.Team?.Name ?? string.Empty,
                    SubmittedByStudentId = submission.SubmittedByStudentId,
                    SubmitedByStudentName = submission.SubmittedByStudent?.User?.Fullname ?? string.Empty,
                    JudgeUserId = judgeUserId,
                    JudgeEmail = judgeEmail,
                    Status = submission.Status ?? string.Empty,
                    CriterionResults = submission.SubmissionDetails?
                        .Where(sd => sd.TestcaseId.HasValue && sd.Testcase != null && !sd.Testcase.DeletedAt.HasValue)
                        .Select(sd => new RubricCriterionResultDTO
                        {
                            RubricId = sd.TestcaseId!.Value,
                            Description = sd.Testcase?.Description ?? sd.Testcase?.Input ?? "Criterion",
                            MaxScore = Math.Round(sd.Testcase?.Weight ?? 0, 2),
                            Score = Math.Round(sd.Weight ?? 0, 2),
                            Note = sd.Note
                        })
                        .ToList() ?? new List<RubricCriterionResultDTO>()
                };

                return dto;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving submission: {ex.Message}");
            }
        }

        public async Task<PaginatedList<PlagiarismQueueItemDTO>> GetPlagiarismQueueAsync(
            int pageNumber,
            int pageSize,
            Guid? contestId,
            Guid? roundId,
            string? studentName,
            string? teamName)
        {
            if (pageNumber < 1 || pageSize < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Page number and page size must be >= 1.");
            }

            var fpRepo = _unitOfWork.GetRepository<SubmissionFingerprint>();

            IQueryable<SubmissionFingerprint> query = fpRepo.Entities
                .Where(f => f.Submission.DeletedAt == null
                            && f.Submission.Status == STATUS_PLAGIARISM_SUSPECTED)
                .Include(f => f.Submission)
                    .ThenInclude(s => s.Problem)
                        .ThenInclude(p => p.Round)
                            .ThenInclude(r => r.Contest)
                .Include(f => f.Submission.Team)
                .Include(f => f.Submission.SubmittedByStudent)
                    .ThenInclude(st => st.User);

            if (!IsAdmin())
            {
                string organizerUserId = GetCurrentUserIdString();

                query = query.Where(f => f.Submission.Problem.Round.Contest.CreatedBy == organizerUserId);
            }


            if (contestId.HasValue)
                query = query.Where(f => f.Submission.Problem.Round.ContestId == contestId.Value);

            if (roundId.HasValue)
                query = query.Where(f => f.Submission.Problem.RoundId == roundId.Value);

            if (!string.IsNullOrWhiteSpace(studentName))
                query = query.Where(f => f.Submission.SubmittedByStudent.User.Fullname.Contains(studentName.Trim()));

            if (!string.IsNullOrWhiteSpace(teamName))
                query = query.Where(f => f.Submission.Team.Name.Contains(teamName.Trim()));

            query = query.OrderByDescending(f => f.Submission.CreatedAt);

            PaginatedList<SubmissionFingerprint> paged = await fpRepo.GetPagingAsync(query, pageNumber, pageSize);

            var items = paged.Items.Select(f => new PlagiarismQueueItemDTO
            {
                SubmissionId = f.SubmissionId,
                ProblemId = f.ProblemId,
                RoundId = f.Submission.Problem.RoundId,
                RoundName = f.Submission.Problem.Round.Name,
                ContestId = f.Submission.Problem.Round.ContestId,
                ContestName = f.Submission.Problem.Round.Contest.Name,

                TeamId = f.TeamId,
                TeamName = f.Submission.Team.Name,

                StudentId = f.Submission.SubmittedByStudentId,
                StudentName = f.Submission.SubmittedByStudent.User.Fullname,

                Score = f.Submission.Score,
                SubmittedAt = f.Submission.CreatedAt,

                Hash = f.Hash,
                Algorithm = f.Algorithm,
                NormalizedLength = f.NormalizedLength
            }).ToList();

            return new PaginatedList<PlagiarismQueueItemDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<PlagiarismSubmissionDetailDTO> GetPlagiarismSubmissionDetailAsync(Guid submissionId)
        {
            var fpRepo = _unitOfWork.GetRepository<SubmissionFingerprint>();

            SubmissionFingerprint? fp = await fpRepo.Entities
                .Where(f => f.SubmissionId == submissionId)
                .Include(f => f.Submission)
                    .ThenInclude(s => s.Problem)
                        .ThenInclude(p => p.Round)
                            .ThenInclude(r => r.Contest)
                .Include(f => f.Submission.Team)
                .Include(f => f.Submission.SubmittedByStudent)
                    .ThenInclude(st => st.User)
                .Include(f => f.Submission.SubmissionArtifacts)
                .Include(f => f.Submission.SubmissionDetails)
                    .ThenInclude(sd => sd.Testcase)
                .OrderByDescending(f => f.CreatedAt)
                .FirstOrDefaultAsync();

            if (fp == null || fp.Submission.DeletedAt != null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Fingerprint/submission {submissionId} not found");
            }

            if (!IsAdmin())
            {
                string organizerUserId = GetCurrentUserIdString();

                if (fp.Submission.Problem?.Round?.Contest == null ||
                    fp.Submission.Problem.Round.Contest.CreatedBy != organizerUserId)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "You do not have permission to view this plagiarism case.");
                }
            }

            if (!string.Equals(fp.Submission.Status, STATUS_PLAGIARISM_SUSPECTED, StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Submission is not in plagiarism suspected state.");
            }

            // Find other submissions with same (ProblemId, Hash) but different TeamId
            List<PlagiarismMatchDTO> matches = await fpRepo.Entities
                .Where(x => x.ProblemId == fp.ProblemId
                            && x.Hash == fp.Hash
                            && x.TeamId != fp.TeamId
                            && x.Submission.DeletedAt == null)
                .Include(x => x.Submission)
                    .ThenInclude(s => s.Team)
                .Include(x => x.Submission)
                    .ThenInclude(s => s.SubmittedByStudent)
                        .ThenInclude(st => st.User)
                .Include(x => x.Submission)
                    .ThenInclude(s => s.SubmissionArtifacts)
                .Include(x => x.Submission)
                    .ThenInclude(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                .OrderByDescending(x => x.Submission.CreatedAt)
                .Select(x => new PlagiarismMatchDTO
                {
                    SubmissionId = x.SubmissionId,
                    TeamId = x.TeamId,
                    TeamName = x.Submission.Team.Name,
                    StudentId = x.Submission.SubmittedByStudentId,
                    StudentName = x.Submission.SubmittedByStudent.User.Fullname,
                    SubmittedAt = x.Submission.CreatedAt,
                    Score = x.Submission.Score,
                    Artifacts = x.Submission.SubmissionArtifacts != null
                        ? x.Submission.SubmissionArtifacts
                            .Where(a => a.DeletedAt == null)
                            .Select(a => _mapper.Map<GetSubmissionArtifactDTO>(a))
                            .ToList()
                        : new List<GetSubmissionArtifactDTO>(),
                    Details = x.Submission.SubmissionDetails != null
                        ? x.Submission.SubmissionDetails
                            .Where(d => d.DeletedAt == null)
                            .Select(d => _mapper.Map<GetSubmissionDetailDTO>(d))
                            .ToList()
                        : new List<GetSubmissionDetailDTO>()
                })
                .ToListAsync();

            var artifacts = fp.Submission.SubmissionArtifacts?
                .Where(a => a.DeletedAt == null)
                .Select(a => _mapper.Map<GetSubmissionArtifactDTO>(a))
                .ToList() ?? new();

            var details = fp.Submission.SubmissionDetails?
                .Where(d => d.DeletedAt == null)
                .Select(d => _mapper.Map<GetSubmissionDetailDTO>(d))
                .ToList() ?? new();

            var head = new PlagiarismQueueItemDTO
            {
                SubmissionId = fp.SubmissionId,
                ProblemId = fp.ProblemId,
                RoundId = fp.Submission.Problem.RoundId,
                RoundName = fp.Submission.Problem.Round.Name,
                ContestId = fp.Submission.Problem.Round.ContestId,
                ContestName = fp.Submission.Problem.Round.Contest.Name,
                TeamId = fp.TeamId,
                TeamName = fp.Submission.Team.Name,
                StudentId = fp.Submission.SubmittedByStudentId,
                StudentName = fp.Submission.SubmittedByStudent.User.Fullname,
                Score = fp.Submission.Score,
                SubmittedAt = fp.Submission.CreatedAt,
                Hash = fp.Hash,
                Algorithm = fp.Algorithm,
                NormalizedLength = fp.NormalizedLength
            };

            return new PlagiarismSubmissionDetailDTO
            {
                Submission = head,
                Artifacts = artifacts,
                Details = details,
                Matches = matches
            };
        }

        public Task ApprovePlagiarismSubmissionAsync(Guid submissionId)
            => ResolvePlagiarismSubmissionAsync(submissionId, cleared: true);

        public Task DenyPlagiarismSubmissionAsync(Guid submissionId)
            => ResolvePlagiarismSubmissionAsync(submissionId, cleared: false);

        private async Task ResolvePlagiarismSubmissionAsync(Guid submissionId, bool cleared)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Get the submission
                Submission? submission = await submissionRepo.Entities
                    .Where(s => s.SubmissionId == submissionId && s.DeletedAt == null)
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                        .ThenInclude(r => r.Contest)
                    .FirstOrDefaultAsync();

                // Existence check
                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission {submissionId} not found");
                }

                // Permission check
                if (!IsAdmin())
                {
                    string organizerUserId = GetCurrentUserIdString();

                    if (submission.Problem?.Round?.Contest == null ||
                        submission.Problem.Round.Contest.CreatedBy != organizerUserId)
                    {
                        throw new ErrorException(StatusCodes.Status403Forbidden,
                            ResponseCodeConstants.FORBIDDEN,
                            "You do not have permission to resolve this plagiarism case.");
                    }
                }

                // Status check
                if (!string.Equals(submission.Status, STATUS_PLAGIARISM_SUSPECTED, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Submission is not in plagiarism suspected state.");
                }

                // staff/admin userId
                string staffUserId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "User ID not found");

                bool isManual = submission.Problem?.Type == ProblemTypeEnum.Manual.ToString();

                if (cleared)
                {
                    submission.JudgedBy = null;
                    if (isManual)
                    {
                        submission.Status = SubmissionStatusEnum.Pending.ToString();
                        submission.Score = 0;
                    }
                    else
                    {
                        // Auto/MCQ: keep finished status and retain existing score
                        submission.Status = SubmissionStatusEnum.Finished.ToString();
                    }
                }
                else
                {
                    submission.JudgedBy = staffUserId;
                    submission.Status = STATUS_PLAGIARISM_CONFIRMED;
                    submission.Score = 0;

                    // Notify team (students + mentor) and organizer about confirmed plagiarism
                    try
                    {
                        var teamRepo = _unitOfWork.GetRepository<Team>();
                        var team = await teamRepo.Entities
                            .Include(t => t.TeamMembers)
                                .ThenInclude(tm => tm.Student)
                                    .ThenInclude(s => s.User)
                            .FirstOrDefaultAsync(t => t.TeamId == submission.TeamId && t.DeletedAt == null);

                        var users = new HashSet<Guid>();
                        if (team != null)
                        {
                            foreach (var tm in team.TeamMembers)
                            {
                                users.Add(tm.Student.UserId);
                            }

                            if (team.MentorId != Guid.Empty)
                            {
                                var mentorRepo = _unitOfWork.GetRepository<Mentor>();
                                Guid? mentorUserId = await mentorRepo.Entities
                                    .Where(m => m.MentorId == team.MentorId && m.DeletedAt == null)
                                    .Select(m => (Guid?)m.UserId)
                                    .FirstOrDefaultAsync();
                                if (mentorUserId.HasValue) users.Add(mentorUserId.Value);
                            }
                        }

                        if (Guid.TryParse(submission.Problem?.Round.Contest.CreatedBy, out Guid organizerId) && organizerId != Guid.Empty)
                        {
                            users.Add(organizerId);
                        }

                        if (users.Count > 0)
                        {
                            await _notificationService.CreateInAppToUsersAsync(
                                users,
                                NotificationTypes.PlagiarismConfirmed,
                                new
                                {
                                    contestId = submission.Problem?.Round.ContestId,
                                    roundId = submission.Problem?.RoundId,
                                    submissionId = submission.SubmissionId,
                                    teamId = submission.TeamId,
                                    status = submission.Status,
                                    message = "Submission was confirmed as plagiarism."
                                });
                        }
                    }
                    catch
                    {
                        // ignore notification failures
                    }
                }

                await submissionRepo.UpdateAsync(submission);
                await _unitOfWork.SaveAsync();

                // Log activity
                if (Guid.TryParse(staffUserId, out var staffUserGuid))
                {
                    await _logWriter.TryWriteAsync(
                        staffUserGuid,
                        ActivityActions.SubmissionStatusChange,
                        TargetTypes.Submission,
                        submission.SubmissionId.ToString());
                }

                Guid roundId = submission.Problem!.RoundId;
                Guid studentId = submission.SubmittedByStudentId;
                Guid contestId = submission.Problem.Round.ContestId;

                if (cleared)
                {
                    if (isManual)
                    {
                        await _configService.ResetDistributionStatusAsync(roundId);
                        try
                        {
                            await _roundService.DistributeSubmissionsToJudgesAsync(roundId);
                        }
                        catch (ErrorException)
                        {
                            // Ignore distribution errors (e.g., no judges) on plagiarism clear
                        }
                    }
                    else
                    {
                        // For auto/MCQ, ensure leaderboard reflects current submission score
                        var lbRepo = _unitOfWork.GetRepository<LeaderboardEntry>();
                        var entry = await lbRepo.Entities.FirstOrDefaultAsync(e => e.ContestId == contestId && e.TeamId == submission.TeamId);
                        if (entry != null)
                        {
                            entry.Score = submission.Score;
                            entry.SnapshotAt = DateTime.UtcNow;
                            await lbRepo.UpdateAsync(entry);
                            await _unitOfWork.SaveAsync();
                            try
                            {
                                await _leaderboardService.RecalculateRanksAsync(contestId);
                            }
                            catch (Exception)
                            {
                                // ignore rank recalculation issues on resolve
                            }
                        }
                    }
                }
                else
                {
                    // On confirmed plagiarism: eliminate team from contest and zero out scoreboard
                    await EliminateTeamForPlagiarismAsync(contestId, submission.TeamId);
                }

                // Notify student
                await TryNotifySubmissionStatusAsync(submission, "Submission status updated.");

                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error resolving plagiarism submission: {ex.Message}");
            }
        }

        private async Task<(bool suspected, Guid? matchedSubmissionId)> CheckAndFlagPlagiarismAsync(
            Submission submission,
            Problem problem,
            string sourceCode)
        {
            string normalized = PlagiarismHelpers.NormalizePython(sourceCode, removeTripleQuoted: true);
            return await CheckAndFlagPlagiarismCoreAsync(submission, problem, normalized);
        }

        private async Task<(bool suspected, Guid? matchedSubmissionId)> CheckAndFlagPlagiarismNormalizedAsync(
            Submission submission,
            Problem problem,
            string normalized)
        {
            return await CheckAndFlagPlagiarismCoreAsync(submission, problem, normalized);
        }

        private async Task<(bool suspected, Guid? matchedSubmissionId)> CheckAndFlagPlagiarismCoreAsync(
            Submission submission,
            Problem problem,
            string normalized)
        {
            // Avoid false positives on tiny/template code
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < MIN_NORMALIZED_LEN_TO_CHECK)
                return (false, null);

            string hash = PlagiarismHelpers.Sha256Hex(normalized);

            var fpRepo = _unitOfWork.GetRepository<SubmissionFingerprint>();

            // Find any existing fingerprint with same Problem + Hash but different Team
            Guid? matchedId = await fpRepo.Entities
                .AsNoTracking()
                .Where(f =>
                    f.ProblemId == problem.ProblemId &&
                    f.Hash == hash &&
                    f.TeamId != submission.TeamId &&
                    f.Submission.DeletedAt == null)
                .Select(f => (Guid?)f.SubmissionId)
                .FirstOrDefaultAsync();

            // Save fingerprint for this submission
            SubmissionFingerprint fp = new SubmissionFingerprint
            {
                FingerprintId = Guid.NewGuid(),
                SubmissionId = submission.SubmissionId,
                ProblemId = problem.ProblemId,
                TeamId = submission.TeamId,
                Algorithm = FP_ALGORITHM,
                Hash = hash,
                NormalizedLength = normalized.Length,
                CreatedAt = DateTime.UtcNow
            };

            bool exists = await fpRepo.Entities.AnyAsync(x => x.SubmissionId == submission.SubmissionId);
            if (!exists)
            {
                await fpRepo.InsertAsync(fp);
            }

            if (matchedId.HasValue)
            {
                var submissionRepo = _unitOfWork.GetRepository<Submission>();

                submission.Status = STATUS_PLAGIARISM_SUSPECTED;
                submission.JudgedBy = null;
                await submissionRepo.UpdateAsync(submission);

                // Notify organizer about suspected plagiarism
                Guid contestId = submission.Problem.Round.ContestId;
                Guid roundId = submission.Problem.RoundId;
                if (Guid.TryParse(submission.Problem.Round.Contest.CreatedBy, out Guid organizerId) && organizerId != Guid.Empty)
                {
                    try
                    {
                        await _notificationService.CreateInAppToUserAsync(
                            organizerId,
                            NotificationTypes.PlagiarismSuspected,
                            new
                            {
                                contestId,
                                roundId,
                                submissionId = submission.SubmissionId,
                                teamId = submission.TeamId,
                                status = submission.Status,
                                message = "A submission was flagged as plagiarism suspected."
                            });
                    }
                    catch
                    {
                        
                    }
                }
            }

            await _unitOfWork.SaveAsync();
            return (matchedId.HasValue, matchedId);
        }

        private async Task EliminateTeamForPlagiarismAsync(Guid contestId, Guid teamId)
        {
            var teamRepo = _unitOfWork.GetRepository<Team>();
            var leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

            Team? team = await teamRepo.Entities
                .FirstOrDefaultAsync(t => t.TeamId == teamId && t.ContestId == contestId && t.DeletedAt == null);

            if (team != null && !string.Equals(team.Status, TeamStatusConstants.Eliminated, StringComparison.OrdinalIgnoreCase))
            {
                team.Status = TeamStatusConstants.Eliminated;
                await teamRepo.UpdateAsync(team);
            }

            LeaderboardEntry? entry = await leaderboardRepo.Entities
                .FirstOrDefaultAsync(e => e.ContestId == contestId && e.TeamId == teamId);

            if (entry != null)
            {
                entry.Score = 0;
                entry.SnapshotAt = DateTime.UtcNow;
                await leaderboardRepo.UpdateAsync(entry);
            }

            // Persist elimination and score reset
            await _unitOfWork.SaveAsync();

            // Update leaderboard ranks
            await _leaderboardService.UpdateContestLeaderboardAsync(contestId);

            // Notify mentor about dashboard update
            if (team?.MentorId != null)
            {
                await _dashboardNotifier.NotifyMentorDashboardUpdatedAsync(team.MentorId);
            }
        }

        private async Task<string?> TryExtractNormalizedPythonFromArchiveAsync(IFormFile archiveFile)
        {
            try
            {
                if (archiveFile == null || archiveFile.Length <= 0) return null;
                if (archiveFile.Length > MAX_ARCHIVE_BYTES) return null;

                string ext = Path.GetExtension(archiveFile.FileName).ToLowerInvariant();
                if (ext != EXTENSION_ZIP && ext != EXTENSION_RAR) return null;

                using var input = archiveFile.OpenReadStream();
                using var ms = new MemoryStream(capacity: (int)Math.Min(archiveFile.Length, int.MaxValue));

                await input.CopyToAsync(ms);
                ms.Position = 0;

                List<string> normalizedPieces = ext == EXTENSION_ZIP
                    ? await ReadZipPythonAsync(ms)
                    : await ReadRarPythonAsync(ms);

                // Keep only meaningful pieces
                normalizedPieces = normalizedPieces
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length >= MIN_NORMALIZED_LEN_TO_CHECK)
                    .ToList();

                if (!normalizedPieces.Any()) return null;

                // Order pieces by hash 
                var ordered = normalizedPieces
                    .Select(s => new { Code = s, H = PlagiarismHelpers.Sha256Hex(s) })
                    .OrderBy(x => x.H, StringComparer.Ordinal)
                    .Select(x => x.Code);

                string combined = string.Concat(ordered);

                if (combined.Length < MIN_NORMALIZED_LEN_TO_CHECK) return null;

                return combined;
            }
            catch
            {
                return null;
            }

        }

        private async Task<List<string>> ReadZipPythonAsync(Stream zipStream)
        {
            if (zipStream == null) return new List<string>();

            zipStream.Position = 0;

            var results = new List<string>();
            long totalBytes = 0;

            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in zip.Entries)
            {
                try
                {
                    if (results.Count >= MAX_PY_FILES) break;
                    if (totalBytes >= MAX_TOTAL_PY_BYTES) break;

                    if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith("/")) continue;
                    if (!entry.FullName.EndsWith(EXTENSION_PY, StringComparison.OrdinalIgnoreCase)) continue;

                    string path = entry.FullName.Replace('\\', '/');
                    if (IGNORE_PATH_CONTAINS.Any(x => path.Contains(x, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (entry.Length <= 0) continue;

                    long remaining = MAX_TOTAL_PY_BYTES - totalBytes;
                    if (remaining <= 0) break;

                    // 
                    if (entry.Length > remaining) continue;

                    if (entry.Length > int.MaxValue) continue;

                    using var entryStream = entry.Open();

                    var (raw, bytesRead) = await ReadAllTextWithinLimitAsync(entryStream, (int)entry.Length);
                    if (raw == null) continue;

                    string normalized = PlagiarismHelpers.NormalizePython(raw, removeTripleQuoted: true);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        results.Add(normalized);

                    totalBytes += bytesRead; // bytesRead ~ entry.Length
                }
                catch
                {
                    continue; 
                }

            }

            return results;
        }
        private async Task<List<string>> ReadRarPythonAsync(Stream rarStream)
        {
            if (rarStream == null) return new List<string>();

            rarStream.Position = 0;

            var results = new List<string>();
            long totalBytes = 0;

            using var archive = ArchiveFactory.Open(rarStream);

            foreach (var entry in archive.Entries)
            {
                try
                {

                    if (results.Count >= MAX_PY_FILES) break;
                    if (totalBytes >= MAX_TOTAL_PY_BYTES) break;

                    if (entry.IsDirectory) continue;
                    if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                    if (!entry.Key.EndsWith(EXTENSION_PY, StringComparison.OrdinalIgnoreCase)) continue;

                    string path = entry.Key.Replace('\\', '/');
                    if (IGNORE_PATH_CONTAINS.Any(x => path.Contains(x, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    long remaining = MAX_TOTAL_PY_BYTES - totalBytes;
                    if (remaining <= 0) break;

                    long? entrySize = null;
                    try { entrySize = (long)entry.Size; } catch { entrySize = null; }

                    if (entrySize.HasValue && entrySize.Value > remaining) continue;

                    int limit = (int)Math.Min(remaining, int.MaxValue);

                    using var entryStream = entry.OpenEntryStream();

                    var (raw, bytesRead) = await ReadAllTextWithinLimitAsync(entryStream, limit);
                    if (raw == null) continue;


                    if (entrySize.HasValue && bytesRead != (int)entrySize.Value)
                    {
                        continue;
                    }

                    string normalized = PlagiarismHelpers.NormalizePython(raw, removeTripleQuoted: true);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        results.Add(normalized);

                    totalBytes += bytesRead;
                }
                catch
                {
                    continue;
                }

            }

            return results;
        }

        private static async Task<(string? Text, int BytesRead)> ReadAllTextWithinLimitAsync(Stream stream, int maxBytes)
        {
            if (stream == null || maxBytes <= 0) return (string.Empty, 0);

            byte[] buffer = new byte[81920];
            int total = 0;

            using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 1024 * 1024));

            while (true)
            {
                int remaining = maxBytes - total;
                if (remaining <= 0)
                {
                    // check if stream still has more data -> too large
                    int extra = await stream.ReadAsync(buffer, 0, 1);
                    if (extra > 0) return (null, total + extra);
                    break;
                }

                int read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read <= 0) break;

                ms.Write(buffer, 0, read);
                total += read;
            }

            byte[] bytes = ms.ToArray();

            // Try UTF-8 strict first, fallback to Latin1
            string text;
            try
            {
                var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                text = utf8Strict.GetString(bytes);
            }
            catch
            {
                text = Encoding.Latin1.GetString(bytes);
            }

            // Remove BOM
            if (!string.IsNullOrEmpty(text) && text[0] == '\uFEFF')
                text = text.TrimStart('\uFEFF');

            return (text, total);
        }

        private async Task<Guid?> TryGetStudentUserIdAsync(Guid studentId)
        {
            var studentRepo = _unitOfWork.GetRepository<Student>();
            return await studentRepo.Entities
                .Where(s => s.StudentId == studentId && s.DeletedAt == null)
                .Select(s => (Guid?)s.UserId)
                .FirstOrDefaultAsync();
        }

        private async Task TryNotifySubmissionResultAsync(Submission submission)
        {
            try
            {
                Guid? userId = await TryGetStudentUserIdAsync(submission.SubmittedByStudentId);
                if (!userId.HasValue) return;

                await _notificationService.CreateInAppToUserAsync(
                    userId.Value,
                    NotificationTypes.SubmissionResult,
                    new
                    {
                        submissionId = submission.SubmissionId,
                        status = submission.Status,
                        score = submission.Score,
                        problemId = submission.ProblemId,
                        teamId = submission.TeamId,
                        targetType = TargetTypes.Submission,
                        targetId = submission.SubmissionId.ToString(),
                        message = "Submission result is available."
                    });

                await _logWriter.TryWriteAsync(
                    userId.Value,
                    ActivityActions.SubmissionStatusChange,
                    TargetTypes.Submission,
                    submission.SubmissionId.ToString());
            }
            catch
            {
            }
        }

        private async Task TryNotifySubmissionStatusAsync(Submission submission, string message)
        {
            try
            {
                Guid? userId = await TryGetStudentUserIdAsync(submission.SubmittedByStudentId);
                if (!userId.HasValue) return;

                await _notificationService.CreateInAppToUserAsync(
                    userId.Value,
                    NotificationTypes.SubmissionStatusChanged,
                    new
                    {
                        submissionId = submission.SubmissionId,
                        status = submission.Status,
                        problemId = submission.ProblemId,
                        teamId = submission.TeamId,
                        targetType = TargetTypes.Submission,
                        targetId = submission.SubmissionId.ToString(),
                        message
                    });
            }
            catch
            {
            }
        }

        public async Task<JudgeSubmissionResultDTO> EvaluateMockTestSubmissionAsync(
            Guid roundId,
            CreateSubmissionDTO submissionDTO,
            TestCaseEvaluationTypeEnum evaluationType)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Validate round deadline
                await ValidateRoundDeadlineAsync(roundId, OPERATION_NAME);

                // Get and validate problem with mock test
                Problem problem = await GetAndValidateMockTestProblemAsync(roundId);

                // Get student and team information
                var (studentId, teamId, contestId) = await GetMockTestSubmitterInfoAsync(roundId);

                // Validate student hasn't finished round
                await ValidateStudentNotFinishedRoundAsync(roundId, studentId);

                // Count previous submissions for penalty calculation
                int previousSubmissionsCount = await CountPreviousSubmissionsAsync(problem.ProblemId, studentId);

                // Process submission artifact (file or code)
                var (sourceCode, artifactType, artifactUrl) = await ProcessSubmissionArtifactAsync(
                    submissionDTO, evaluationType, studentId);

                // Create submission record with artifact
                Submission submission = await CreateMockTestSubmissionAsync(
                    teamId, problem.ProblemId, studentId, artifactType, artifactUrl);

                // Execute mock tests
                JudgeSubmissionResultDTO mockResult = await _mockTestExecutor.ExecuteMockTestAsync(
                    sourceCode,
                    problem.MockTestUrl!,
                    timeLimitSec: MOCK_TEST_EXECUTION_TIME_LIMIT_SECONDS,
                    memoryLimitMb: MOCK_TEST_EXECUTION_MEMORY_LIMIT
                );

                // Populate metadata
                mockResult.SubmissionId = submission.SubmissionId.ToString();
                mockResult.ProblemId = problem.ProblemId.ToString();
                mockResult.Language = problem.Language;

                // Check for critical errors
                bool hasCriticalError = mockResult.Summary.Total == 0 ||
                                        mockResult.Cases.All(c => c.Status == "error");

                if (hasCriticalError)
                {
                    // Handle failed mock test
                    JudgeSubmissionResultDTO failedResult = await HandleFailedMockTestAsync(submission, mockResult);
                    _unitOfWork.CommitTransaction();
                    return failedResult;
                }

                // Apply penalty and save
                await SaveMockTestResultAsync(
                    submission.SubmissionId,
                    mockResult,
                    previousSubmissionsCount,
                    problem.PenaltyRate);

                // Check plagiarism
                await CheckAndFlagPlagiarismAsync(submission, problem, sourceCode);

                _unitOfWork.CommitTransaction();

                return mockResult;
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();
                if (ex is ErrorException) throw;
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error evaluating mock test submission: {ex.Message}");
            }
        }

        private async Task SaveMockTestResultAsync(
            Guid submissionId,
            JudgeSubmissionResultDTO mockResult,
            int previousSubmissionsCount,
            double? penaltyRate)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            Submission? submission = await submissionRepo.Entities
                .Include(s => s.Problem)
                    .ThenInclude(p => p.Round)
                .Where(s => s.SubmissionId == submissionId)
                .FirstOrDefaultAsync();

            if (submission == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Submission {submissionId} not found");
            }

            // Get round weight from config for mock test rounds
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            string weightKey = ConfigKeys.RoundWeight(submission.Problem.RoundId);

            Config? weightConfig = await configRepo.Entities
                .Where(c => c.Key == weightKey && c.Scope == SCOPE_ROUND && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            // Default max possible score
            double maxPossibleScore = double.TryParse(weightConfig?.Value, out double configuredWeight) 
                ? configuredWeight 
                : DEFAULT_MAX_SCORE;

            // Calculate raw score: (passed / total) * maxPossibleScore
            double totalTests = mockResult.Summary.Total;
            double passedTests = mockResult.Summary.Passed;
            double rawScore = totalTests > 0 ? (passedTests / totalTests) * maxPossibleScore : 0;

            // Apply penalty
            double finalScore = rawScore;
            if (penaltyRate.HasValue && previousSubmissionsCount > 0)
            {
                double penaltyPercentage = penaltyRate.Value * previousSubmissionsCount;
                double penaltyAmount = rawScore * penaltyPercentage;
                finalScore = Math.Max(0, rawScore - penaltyAmount);
            }

            // Update submission
            submission.Status = SubmissionStatusEnum.Finished.ToString();
            submission.Score = Math.Round(finalScore, 2);

            // Update summary with scores
            mockResult.Summary.rawScore = Math.Round(rawScore, 2);
            mockResult.Summary.penaltyScore = Math.Round(finalScore, 2);

            // Save mock test details as submission details
            IGenericRepository<SubmissionDetail> detailRepo = _unitOfWork.GetRepository<SubmissionDetail>();
            double weightPerTest = totalTests > 0 ? (maxPossibleScore / totalTests) : 0;

            foreach (JudgeCaseResultDTO testCase in mockResult.Cases)
            {
                SubmissionDetail submissionDetail = new SubmissionDetail
                {
                    DetailsId = Guid.NewGuid(),
                    SubmissionId = submissionId,
                    TestcaseId = null,
                    Weight = weightPerTest,
                    Note = string.IsNullOrEmpty(testCase.Stderr)
                        ? $"{testCase.Id}: {testCase.Status}"
                        : $"{testCase.Id}: {testCase.Status} - {testCase.Stderr}",
                    RuntimeMs = 0,
                    MemoryKb = testCase.MemoryKb ?? 0,
                    CreatedAt = DateTime.UtcNow
                };

                await detailRepo.InsertAsync(submissionDetail);
            }

            await submissionRepo.UpdateAsync(submission);
            await _unitOfWork.SaveAsync();
        }

        private sealed class TeamRankRow
        {
            public Guid TeamId { get; set; }
            public double AvgScore { get; set; }
            public double AvgCreatedAtTicks { get; set; }
        }

        // Get the rank cutoff config for a round
        private async Task<int> GetRoundRankCutoffAsync(Guid roundId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            string key = ConfigKeys.RoundRankCutoff(roundId);

            string? value = await configRepo.Entities
                .AsNoTracking()
                .Where(c => c.Key == key && c.Scope == SCOPE_CONTEST && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(value)) return 0;
            return (int.TryParse(value, out int n) && n > 0) ? n : 0;
        }

        // Find the most recent main round before the current round in the same contest
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
                .FirstOrDefaultAsync();
        }
        // Get the top N teams by average score and average submission time in a given round

        private async Task<List<Guid>> GetTopTeamsByRoundAsync(Guid contestId, Guid prevRoundId, int cutoff)
        {
            if (cutoff <= 0) return new List<Guid>();

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

            var latestByTeamStudent = subs
                .GroupBy(x => (x.TeamId, x.SubmittedByStudentId))
                .Select(g => g.OrderByDescending(x => x.CreatedAt)
                              .ThenByDescending(x => x.SubmissionId)
                              .First())
                .ToDictionary(x => (x.TeamId, x.SubmittedByStudentId), x => x);

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
                        if (finished) sumScore += last.Score;
                        sumTicks += finished ? last.CreatedAt.Ticks : DateTime.MaxValue.Ticks;
                    }
                    else
                    {
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

        // Ensure that a team is eligible to submit in a round based on previous round rankings
        private async Task EnsureTeamEligibleForRoundAsync(Guid roundId, Guid teamId)
        {
            int cutoff = await GetRoundRankCutoffAsync(roundId);
            if (cutoff <= 0) return;

            var roundRepo = _unitOfWork.GetRepository<Round>();
            Round? currentRound = await roundRepo.Entities
                .AsNoTracking()
                .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (currentRound == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            if (currentRound.IsRetakeRound) return;

            Round? prevRound = await FindPreviousMainRoundAsync(currentRound);
            if (prevRound == null) return;

            // If retake exists, enforce only after retake finalized; otherwise after main finalized
            Round? retakeRound = await FindRetakeRoundAsync(prevRound);
            var submissionRepo = _unitOfWork.GetRepository<Submission>();
            var appealRepo = _unitOfWork.GetRepository<Appeal>();

            if (retakeRound != null)
            {
                bool retakeFinalized = await IsRoundFinalizedAsync(retakeRound.RoundId, submissionRepo, appealRepo);
                if (!retakeFinalized) return;
                prevRound = retakeRound;
            }
            else
            {
                bool prevFinalized = await IsRoundFinalizedAsync(prevRound.RoundId, submissionRepo, appealRepo);
                if (!prevFinalized) return;
            }

            List<Guid> topTeamIds = await GetTopTeamsByRoundAsync(currentRound.ContestId, prevRound.RoundId, cutoff);

            if (!topTeamIds.Contains(teamId))
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                    $"Your team is not in Top-{cutoff} of the previous round.");
        }

        private async Task<Round?> FindRetakeRoundAsync(Round mainRound)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();
            return await roundRepo.Entities
                .AsNoTracking()
                .Where(r => r.IsRetakeRound
                            && r.MainRoundId == mainRound.RoundId
                            && r.DeletedAt == null)
                .OrderBy(r => r.Start)
                .FirstOrDefaultAsync();
        }

        private static async Task<bool> IsRoundFinalizedAsync(
            Guid roundId,
            IGenericRepository<Submission> submissionRepo,
            IGenericRepository<Appeal> appealRepo)
        {
            bool hasPendingSubs = await submissionRepo.Entities
                .AsNoTracking()
                .AnyAsync(s =>
                    s.DeletedAt == null
                    && s.Problem != null
                    && s.Problem.RoundId == roundId
                    && s.Status == SubmissionStatusEnum.Pending.ToString());

            if (hasPendingSubs) return false;

            bool hasOpenAppeals = await appealRepo.Entities
                .AsNoTracking()
                .AnyAsync(a =>
                    a.DeletedAt == null
                    && a.TargetId == roundId
                    && (a.State != AppealStateEnum.Closed.ToString()
                        || a.Decision == AppealDecisionEnum.Pending.ToString()));

            return !hasOpenAppeals;
        }

        public async Task<GetSubmissionDTO> GetAutoTestResultsBySubmissionIdAsync(Guid submissionId)
        {
            try
            {
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Get the submission with all required includes
                Submission? submission = await submissionRepo.Entities
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .Where(s => s.SubmissionId == submissionId
                        && s.Problem.Type == PROBLEM_TYPE_AUTO_EVALUATION
                        && !s.DeletedAt.HasValue)
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .Include(s => s.SubmissionArtifacts)
                    .FirstOrDefaultAsync();

                // Validate submission existence
                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Auto-evaluation submission with ID {submissionId} not found");
                }

                // Calculate attempt number with a separate query
                int attemptNumber = await submissionRepo.Entities
                    .Where(s => s.Problem.RoundId == submission.Problem.RoundId
                        && s.SubmittedByStudentId == submission.SubmittedByStudentId
                        && s.Problem.Type == PROBLEM_TYPE_AUTO_EVALUATION
                        && !s.DeletedAt.HasValue
                        && s.CreatedAt <= submission.CreatedAt)
                    .CountAsync();

                // Map to DTO
                GetSubmissionDTO dto = _mapper.Map<GetSubmissionDTO>(submission);
                dto.TeamName = submission.Team?.Name ?? string.Empty;
                dto.SubmittedByStudentName = submission.SubmittedByStudent?.User?.Fullname ?? string.Empty;
                dto.submissionAttemptNumber = attemptNumber;

                // Map test case details to DTOs
                if (submission?.SubmissionDetails != null)
                {
                    dto.Details = submission.SubmissionDetails
                        .Select(detail => new GetSubmissionDetailDTO
                        {
                            DetailsId = detail.DetailsId,
                            CreatedAt = detail.CreatedAt,
                            SubmissionId = detail.SubmissionId,
                            TestcaseId = detail.TestcaseId ?? Guid.Empty,
                            Weight = Math.Round(detail.Weight ?? 0, 2),
                            Note = detail.Note,
                            RuntimeMs = detail.RuntimeMs,
                            MemoryKb = detail.MemoryKb
                        })
                        .ToList();
                }
                else
                {
                    dto.Details = null;
                }

                // Map Artifacts to DTOs
                if (submission?.SubmissionArtifacts != null)
                {
                    dto.Artifacts = submission.SubmissionArtifacts
                        .Select(artifact => _mapper.Map<GetSubmissionArtifactDTO>(artifact))
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
                    $"Error retrieving auto test result by submission ID: {ex.Message}");
            }
        }

        public async Task<RubricEvaluationResultDTO> GetManualTestResultsBySubmissionIdAsync(Guid submissionId)
        {
            try
            {
                // Get the submission for the specified submission ID
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                Submission? submission = await submissionRepo.Entities
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .Include(s => s.SubmissionDetails)
                        .ThenInclude(sd => sd.Testcase)
                    .Where(s => s.SubmissionId == submissionId
                        && s.Problem.Type == ProblemTypeEnum.Manual.ToString()
                        && !s.DeletedAt.HasValue)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User)
                    .Include(s => s.Team)
                    .FirstOrDefaultAsync();

                // Validate submission existence
                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Manual evaluation submission with ID {submissionId} not found");
                }

                // Get all rubric criteria for max scores
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();
                List<TestCase> rubricCriteria = await rubricRepo.Entities
                    .Where(tc => tc.ProblemId == submission.ProblemId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString()
                        && !tc.DeletedAt.HasValue)
                    .ToListAsync();

                // Map submission details to criterion results
                List<RubricCriterionResultDTO> results = submission.SubmissionDetails
                    .Where(sd => sd.TestcaseId.HasValue && sd.Testcase != null && !sd.Testcase.DeletedAt.HasValue)
                    .Select(d => new RubricCriterionResultDTO
                    {
                        RubricId = d.TestcaseId!.Value,
                        Description = d.Testcase?.Description ?? d.Testcase?.Input ?? "Criterion",
                        MaxScore = Math.Round(d.Testcase?.Weight ?? 0, 2),
                        Score = Math.Round(d.Weight ?? 0, 2),
                        Note = d.Note
                    })
                    .ToList();

                // Get judge email if judge ID exists
                string judgeEmail = "Not yet evaluated";
                if (!string.IsNullOrEmpty(submission.JudgedBy) && Guid.TryParse(submission.JudgedBy, out Guid judgeGuid))
                {
                    IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
                    judgeEmail = await userRepo.Entities
                        .Where(u => u.UserId == judgeGuid && !u.DeletedAt.HasValue)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync() ?? submission.JudgedBy;
                }

                // Create result DTO
                RubricEvaluationResultDTO result = new RubricEvaluationResultDTO
                {
                    SubmissionId = submission.SubmissionId,
                    StudentName = submission.SubmittedByStudent?.User?.Fullname ?? "Unknown",
                    TeamName = submission.Team?.Name ?? "Unknown",
                    SubmittedAt = submission.CreatedAt,
                    JudgedBy = judgeEmail,
                    TotalScore = Math.Round(submission.Score, 2),
                    MaxPossibleScore = Math.Round(rubricCriteria.Sum(tc => tc.Weight), 2),
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
                    $"Error retrieving manual test result by submission ID: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets problem for the specified round
        /// </summary>
        private async Task<Problem> GetProblemForRoundAsync(Guid roundId)
        {
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();

            Problem? problem = await problemRepo.Entities
                .Where(p => p.RoundId == roundId)
                .FirstOrDefaultAsync();

            if (problem == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"The round {roundId} does not have problem");
            }

            return problem;
        }

        /// <summary>
        /// Gets test cases for the specified problem
        /// </summary>
        private async Task<IList<TestCase>> GetTestCasesForProblemAsync(Guid problemId)
        {
            IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

            IList<TestCase> testCases = await testCaseRepo.Entities
                .Where(tc => tc.ProblemId == problemId && tc.Type == TESTCASE_TYPE_TESTCASE)
                .ToListAsync();

            if (!testCases.Any())
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"No test cases found for problem {problemId}");
            }

            return testCases;
        }

        /// <summary>
        /// Gets current student and team information
        /// </summary>
        private async Task<(Guid StudentId, Guid TeamId, Guid ContestId)> GetStudentAndTeamInfoAsync(Guid roundId)
        {
            // Get user ID from JWT token
            string userId = GetCurrentUserIdOrThrow();

            // Get student ID
            Guid studentId = await GetStudentIdFromUserIdAsync(userId);

            // Get contest ID
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Guid contestId = await contestRepo.Entities
                .Where(c => c.Rounds.Any(r => r.RoundId == roundId) && !c.DeletedAt.HasValue)
                .Select(c => c.ContestId)
                .FirstOrDefaultAsync();

            // Get team ID
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
            Guid teamId = await teamRepo.Entities
                .Where(t => t.TeamMembers.Any(tm => tm.StudentId == studentId)
                    && !t.DeletedAt.HasValue
                    && t.ContestId == contestId)
                .Select(t => t.TeamId)
                .FirstOrDefaultAsync();

            return (studentId, teamId, contestId);
        }

        /// <summary>
        /// Validates student hasn't finished the round
        /// </summary>
        private async Task ValidateStudentNotFinishedRoundAsync(Guid roundId, Guid studentId)
        {
            bool isAlreadyFinished = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

            if (isAlreadyFinished)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Cannot execute code. You have already finished this round.");
            }
        }

        /// <summary>
        /// Counts previous submissions for student
        /// </summary>
        private async Task<int> CountPreviousSubmissionsAsync(Guid problemId, Guid studentId)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            return await submissionRepo.Entities
                .Where(s => s.ProblemId == problemId
                    && s.SubmittedByStudentId == studentId
                    && s.DeletedAt == null)
                .CountAsync();
        }

        /// <summary>
        /// Processes submission artifact (file or code) and returns source code, type, and URL
        /// </summary>
        private async Task<(string SourceCode, string ArtifactType, string ArtifactUrl)> ProcessSubmissionArtifactAsync(
            CreateSubmissionDTO submissionDTO,
            TestCaseEvaluationTypeEnum evaluationType,
            Guid studentId)
        {
            if (evaluationType == TestCaseEvaluationTypeEnum.File)
            {
                return await ProcessFileSubmissionAsync(submissionDTO.File);
            }
            else
            {
                return await ProcessCodeSubmissionAsync(submissionDTO.Code, studentId);
            }
        }

        /// <summary>
        /// Processes file-based submission
        /// </summary>
        private async Task<(string SourceCode, string ArtifactType, string ArtifactUrl)> ProcessFileSubmissionAsync(
            IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "File is required for File evaluation type");
            }

            // Validate file type
            ValidatePythonFileExtension(file.FileName);

            // Upload file to Cloudinary
            string artifactUrl = await _cloudinaryService.UploadFileAsync(file, AUTO_TEST_SUBMISSION_FOLDER);

            // Download file content from Cloudinary URL
            string sourceCode = await SubmissionHelpers.DownloadFileContentAsync(artifactUrl);

            return (sourceCode, FILE_ARTIFACT_TYPE, artifactUrl);
        }

        /// <summary>
        /// Processes code-based submission
        /// </summary>
        private async Task<(string SourceCode, string ArtifactType, string ArtifactUrl)> ProcessCodeSubmissionAsync(
            string? code,
            Guid studentId)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Code is required for Code evaluation type");
            }

            // Unescape the code
            string sourceCode = System.Text.RegularExpressions.Regex.Unescape(code);

            // Upload code as file
            string fileName = $"code_{studentId}_{DateTime.UtcNow:yyyyMMddHHmmss}.py";
            string artifactUrl = await UploadCodeAsFileAsync(code, fileName);

            return (sourceCode, CODE_ARTIFACT_TYPE, artifactUrl);
        }

        /// <summary>
        /// Validates Python file extension
        /// </summary>
        private void ValidatePythonFileExtension(string fileName)
        {
            string fileExtension = Path.GetExtension(fileName).ToLower();
            List<string> allowedExtensions = new List<string> { EXTENSION_PY, EXTENSION_PYTHON };

            if (!allowedExtensions.Contains(fileExtension))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"File type {fileExtension} is not supported. Allowed types: {string.Join(", ", allowedExtensions)}");
            }
        }

        /// <summary>
        /// Creates submission record and artifact
        /// </summary>
        private async Task<Submission> CreateSubmissionRecordAsync(
            Guid teamId,
            Guid problemId,
            Guid studentId,
            string artifactType,
            string artifactUrl)
        {
            // Create submission
            Submission submission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = teamId,
                ProblemId = problemId,
                SubmittedByStudentId = studentId,
                JudgedBy = DEFAULT_JUDGED_BY,
                Status = SUBMISSION_STATUS_PENDING,
                Score = 0,
                CreatedAt = DateTime.UtcNow
            };

            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            await submissionRepo.InsertAsync(submission);

            // Save artifact
            await SaveSubmissionArtifactAsync(submission.SubmissionId, artifactType, artifactUrl);

            await _unitOfWork.SaveAsync();

            return submission;
        }

        /// <summary>
        /// Saves submission artifact
        /// </summary>
        private async Task SaveSubmissionArtifactAsync(Guid submissionId, string artifactType, string artifactUrl)
        {
            IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();

            SubmissionArtifact artifact = new SubmissionArtifact
            {
                ArtifactId = Guid.NewGuid(),
                SubmissionId = submissionId,
                Type = artifactType,
                Url = artifactUrl,
                CreatedAt = DateTime.UtcNow
            };

            await artifactRepo.InsertAsync(artifact);
        }

        /// <summary>
        /// Logs submission creation activity
        /// </summary>
        private async Task LogSubmissionCreationAsync(Guid submissionId)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (Guid.TryParse(userId, out var actorUserId))
            {
                await _logWriter.TryWriteAsync(
                    actorUserId,
                    ActivityActions.SubmissionCreate,
                    TargetTypes.Submission,
                    submissionId.ToString());
            }
        }

        /// <summary>
        /// Evaluates submission using Judge0 service
        /// </summary>
        private async Task<JudgeSubmissionResultDTO> EvaluateWithJudge0Async(
            Problem problem,
            IList<TestCase> testCases,
            string sourceCode,
            Guid submissionId)
        {
            // Build Judge0 request
            JudgeSubmissionRequestDTO judge0Request = BuildJudge0Request(problem, testCases, sourceCode);

            // Auto evaluate submission
            JudgeSubmissionResultDTO result = await _judge0Service.AutoEvaluateSubmissionAsync(judge0Request);

            // Set submission ID in result
            result.SubmissionId = submissionId.ToString();

            return result;
        }

        /// <summary>
        /// Builds Judge0 request DTO
        /// </summary>
        private JudgeSubmissionRequestDTO BuildJudge0Request(
            Problem problem,
            IList<TestCase> testCases,
            string sourceCode)
        {
            return new JudgeSubmissionRequestDTO
            {
                LanguageId = SubmissionHelpers.ConvertToJudge0LanguageId(problem.Language),
                Code = sourceCode,
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
                TimeLimitSec = testCases.Max(tc => tc.TimeLimitMs) / 1000.0 ?? DEFAULT_TIMELIMIT,
                MemoryLimitKb = testCases.Max(tc => tc.MemoryKb) ?? DEFAULT_MEMORY
            };
        }

        /// <summary>
        /// Validates submission file
        /// </summary>
        private void ValidateSubmissionFile(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "No file was provided");
            }

            // Validate file type
            List<string> allowedExtensions = new List<string> { EXTENSION_ZIP, EXTENSION_RAR };
            string fileExtension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"File type {fileExtension} is not supported. Allowed types: {string.Join(", ", allowedExtensions)}");
            }
        }

        /// <summary>
        /// Gets current student ID from JWT token
        /// </summary>
        private async Task<Guid> GetCurrentStudentIdAsync()
        {
            string userId = GetCurrentUserIdOrThrow();

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

            return studentId;
        }

        /// <summary>
        /// Gets team ID for student in specified round
        /// </summary>
        private async Task<Guid> GetTeamIdForStudentInRoundAsync(Guid studentId, Guid roundId)
        {
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            return await teamRepo.Entities
                .Where(t => t.TeamMembers.Any(tm => tm.StudentId == studentId)
                    && !t.DeletedAt.HasValue
                    && t.Contest.Rounds.Any(r => r.RoundId == roundId))
                .Select(t => t.TeamId)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gets problem ID for the specified round
        /// </summary>
        private async Task<Guid> GetProblemIdForRoundAsync(Guid roundId)
        {
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

            return await roundRepo.Entities
                .Where(r => r.RoundId == roundId)
                .Select(r => r.Problem!.ProblemId)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Deletes previous submissions for team in round
        /// </summary>
        private async Task DeletePreviousSubmissionsAsync(Guid roundId, Guid teamId)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            List<Submission>? previousSubmissions = await submissionRepo.Entities
                .Where(s => s.Problem.Round.RoundId == roundId
                    && s.TeamId == teamId
                    && !s.DeletedAt.HasValue)
                .ToListAsync();

            if (previousSubmissions == null || !previousSubmissions.Any())
                return;

            // Delete artifacts
            await DeleteSubmissionArtifactsAsync(previousSubmissions);

            // Mark submissions as deleted
            foreach (Submission item in previousSubmissions)
            {
                item.DeletedAt = DateTime.UtcNow;
                await submissionRepo.UpdateAsync(item);
            }

            await _unitOfWork.SaveAsync();
        }

        /// <summary>
        /// Deletes submission artifacts from cloud storage
        /// </summary>
        private async Task DeleteSubmissionArtifactsAsync(List<Submission> submissions)
        {
            IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();

            List<Guid> submissionIds = submissions.Select(s => s.SubmissionId).ToList();

            List<SubmissionArtifact> artifacts = await artifactRepo.Entities
                .Where(a => submissionIds.Contains(a.SubmissionId)
                    && a.Type == FILE_ARTIFACT_TYPE
                    && a.DeletedAt == null)
                .ToListAsync();

            foreach (SubmissionArtifact art in artifacts)
            {
                await TryDeleteCloudinaryFileAsync(art.Url);

                art.DeletedAt = DateTime.UtcNow;
                await artifactRepo.UpdateAsync(art);
            }
        }

        /// <summary>
        /// Attempts to delete file from Cloudinary
        /// </summary>
        private async Task TryDeleteCloudinaryFileAsync(string url)
        {
            try
            {
                string? publicId = CloudinaryHelpers.ExtractCloudinaryPublicId(url);
                if (!string.IsNullOrWhiteSpace(publicId))
                {
                    await _cloudinaryService.DeleteFileAsync(publicId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to delete file from Cloudinary: {Url}", url);
            }
        }

        /// <summary>
        /// Creates file submission record
        /// </summary>
        private async Task<Submission> CreateFileSubmissionRecordAsync(
            Guid teamId,
            Guid problemId,
            Guid studentId,
            string fileUrl)
        {
            // Create submission
            Submission submission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = teamId,
                ProblemId = problemId,
                SubmittedByStudentId = studentId,
                JudgedBy = null,
                Status = SUBMISSION_STATUS_PENDING,
                Score = 0,
                CreatedAt = DateTime.UtcNow
            };

            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            await submissionRepo.InsertAsync(submission);

            // Save artifact
            await SaveSubmissionArtifactAsync(submission.SubmissionId, FILE_ARTIFACT_TYPE, fileUrl);

            await _unitOfWork.SaveAsync();

            // Log activity
            await LogSubmissionCreationAsync(submission.SubmissionId);

            return submission;
        }

        /// <summary>
        /// Checks plagiarism for archive submissions
        /// </summary>
        private async Task CheckPlagiarismForArchiveAsync(Submission submission, IFormFile file)
        {
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
            Problem? problem = await problemRepo.Entities
                .Include(p => p.Round)
                    .ThenInclude(r => r.Contest)
                .Where(p => p.ProblemId == submission.ProblemId && p.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (problem == null) return;

            // hydrate navigation needed for notification
            submission.Problem = problem;
            string? combinedNormalized = await TryExtractNormalizedPythonFromArchiveAsync(file);

            if (!string.IsNullOrWhiteSpace(combinedNormalized))
            {
                await CheckAndFlagPlagiarismNormalizedAsync(submission, problem, combinedNormalized);
            }
        }

        /// <summary>
        /// Gets submission for rubric evaluation with validation
        /// </summary>
        private async Task<Submission> GetSubmissionForRubricEvaluationAsync(Guid submissionId)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            Submission? submission = await submissionRepo.Entities
                .Include(s => s.Problem)
                    .ThenInclude(p => p.Round)
                .Where(s => s.SubmissionId == submissionId)
                .FirstOrDefaultAsync();

            if (submission == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Submission with ID {submissionId} not found");
            }

            if (submission.Problem.Type != PROBLEM_TYPE_MANUAL)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Rubric evaluation is only available for manual problem types");
            }

            return submission;
        }

        /// <summary>
        /// Gets rubric criteria for problem
        /// </summary>
        private async Task<List<TestCase>> GetRubricCriteriaAsync(Guid problemId)
        {
            IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();

            List<TestCase> criteria = await rubricRepo.Entities
                .Where(tc => tc.ProblemId == problemId
                    && tc.Type == TESTCASE_TYPE_MANUAL
                    && !tc.DeletedAt.HasValue)
                .ToListAsync();

            if (!criteria.Any())
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "No rubric criteria found for this problem");
            }

            return criteria;
        }

        /// <summary>
        /// Validates all rubric criteria are scored
        /// </summary>
        private void ValidateAllCriteriaScored(
            List<TestCase> rubricCriteria,
            List<RubricCriterionScoreDTO> criterionScores)
        {
            HashSet<Guid> submittedCriteriaIds = criterionScores
                .Select(cs => cs.RubricId)
                .ToHashSet();

            HashSet<Guid> allRequiredCriteriaIds = rubricCriteria
                .Select(rc => rc.TestCaseId)
                .ToHashSet();

            List<Guid> missingCriteriaIds = allRequiredCriteriaIds
                .Except(submittedCriteriaIds)
                .ToList();

            if (missingCriteriaIds.Any())
            {
                List<string> missingDescriptions = rubricCriteria
                    .Where(rc => missingCriteriaIds.Contains(rc.TestCaseId))
                    .Select(rc => rc.Description ?? "Unnamed criterion")
                    .ToList();

                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"All criteria must be scored. Missing scores for {missingCriteriaIds.Count} criterion/criteria: {string.Join(", ", missingDescriptions)}");
            }

            // Check for duplicates
            if (criterionScores.Count != submittedCriteriaIds.Count)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Duplicate criteria found in submission. Each criterion should be scored only once.");
            }
        }

        /// <summary>
        /// Processes and saves criterion scores
        /// </summary>
        private async Task<(double TotalScore, List<RubricCriterionResultDTO> Results)> ProcessCriterionScoresAsync(
            Guid submissionId,
            List<RubricCriterionScoreDTO> criterionScores,
            List<TestCase> rubricCriteria)
        {
            Dictionary<Guid, TestCase> rubricsDict = rubricCriteria.ToDictionary(tc => tc.TestCaseId);
            IGenericRepository<SubmissionDetail> detailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

            double totalScore = 0;
            List<RubricCriterionResultDTO> results = new List<RubricCriterionResultDTO>();

            foreach (RubricCriterionScoreDTO criterionScore in criterionScores)
            {
                // Validate criterion exists
                if (!rubricsDict.TryGetValue(criterionScore.RubricId, out TestCase? criterion))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Rubric {criterionScore.RubricId} not found or does not belong to this problem");
                }

                // Validate score
                ValidateCriterionScore(criterionScore.Score, criterion);

                // Save or update submission detail
                await UpsertSubmissionDetailAsync(submissionId, criterionScore, detailRepo);

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

            return (totalScore, results);
        }

        /// <summary>
        /// Validates criterion score
        /// </summary>
        private void ValidateCriterionScore(double score, TestCase criterion)
        {
            if (score > criterion.Weight)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Score {score} exceeds max score {criterion.Weight} for criterion: {criterion.Description}");
            }

            if (score < 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Score cannot be negative for criterion: {criterion.Description}");
            }
        }

        /// <summary>
        /// Upserts submission detail for criterion
        /// </summary>
        private async Task UpsertSubmissionDetailAsync(
            Guid submissionId,
            RubricCriterionScoreDTO criterionScore,
            IGenericRepository<SubmissionDetail> detailRepo)
        {
            SubmissionDetail? existingDetail = await detailRepo.Entities
                .FirstOrDefaultAsync(sd => sd.SubmissionId == submissionId
                    && sd.TestcaseId == criterionScore.RubricId);

            if (existingDetail != null)
            {
                existingDetail.Weight = criterionScore.Score;
                existingDetail.Note = criterionScore.Note;
                await detailRepo.UpdateAsync(existingDetail);
            }
            else
            {
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
        }

        /// <summary>
        /// Gets current judge email
        /// </summary>
        private async Task<string> GetCurrentJudgeEmailAsync()
        {
            string userId = GetCurrentUserIdOrThrow();

            IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
            User? user = await userRepo.GetByIdAsync(Guid.Parse(userId));

            return user?.Email ?? "Unknown Judge";
        }

        private async Task EnsureJudgeDeadlineAsync(Submission submission, string judgeUserId)
        {
            if (!Guid.TryParse(judgeUserId, out var judgeId))
                return;

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            string key = ConfigKeys.JudgeSubmissionDeadline(judgeId, submission.SubmissionId);

            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            DateTime? deadline = null;
            if (DateTime.TryParse(value, out DateTime parsed))
                deadline = parsed;

            if (!deadline.HasValue)
            {
                int rescoreDays = await GetContestPolicyDaysAsync(
                    submission.Problem.Round.ContestId,
                    ContestPolicyKeys.JudgeRescoreDays,
                    DEFAULT_JUDGE_RESCORE_DAYS,
                    configRepo);
                deadline = submission.Problem.Round.End.AddDays(rescoreDays);
            }

            if (DateTime.UtcNow > deadline.Value)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    "JUDGE_DEADLINE_PASSED",
                    "Judge scoring deadline has passed.");
            }
        }

        private static async Task<int> GetContestPolicyDaysAsync(
            Guid contestId,
            string policyKey,
            int defaultDays,
            IGenericRepository<Config> configRepo)
        {
            string key = ConfigKeys.ContestPolicy(contestId, policyKey);
            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return int.TryParse(value, out int days) && days >= 0 ? days : defaultDays;
        }

        /// <summary>
        /// Updates submission with rubric evaluation results
        /// </summary>
        private async Task UpdateSubmissionWithRubricScoreAsync(
            Submission submission,
            double totalScore)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            submission.Score = Math.Round(totalScore, 2);
            submission.Status = SUBMISSION_STATUS_FINISHED;

            await submissionRepo.UpdateAsync(submission);
            await _unitOfWork.SaveAsync();

            await TryNotifySubmissionResultAsync(submission);
        }

        /// <summary>
        /// Updates leaderboard after rubric evaluation
        /// </summary>
        private async Task UpdateLeaderboardAfterRubricEvaluationAsync(Submission submission)
        {
            try
            {
                Guid contestId = submission.Problem.Round.ContestId;

                // Check if there are any pending submissions in the contest
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                bool hasPendingSubmissions = await submissionRepo.Entities
                    .AnyAsync(s => s.DeletedAt == null
                        && s.Problem.Round.ContestId == contestId
                        && s.Status == SubmissionStatusEnum.Pending.ToString());

                if (!hasPendingSubmissions)
                {
                    // Update the entire contest leaderboard if no pending submissions
                    await _leaderboardService.UpdateContestLeaderboardAsync(contestId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to update leaderboard for submission {SubmissionId}",
                    submission.SubmissionId);
            }
        }

        /// <summary>
        /// Gets current user ID from JWT token or throws
        /// </summary>
        private string GetCurrentUserIdOrThrow()
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "User ID not found");
            }

            return userId;
        }

        /// <summary>
        /// Gets student ID from user ID
        /// </summary>
        private async Task<Guid> GetStudentIdFromUserIdAsync(string userId)
        {
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

            return studentId;
        }

        /// <summary>
        /// Checks if current user is admin
        /// </summary>
        private bool IsAdmin()
        {
            return _httpContextAccessor.HttpContext?.User?.IsInRole("Admin") == true;
        }

        /// <summary>
        /// Gets current user ID as string
        /// </summary>
        private string GetCurrentUserIdString()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new ErrorException(StatusCodes.Status401Unauthorized,
                    ResponseCodeConstants.UNAUTHORIZED,
                    "User ID not found.");
        }

        /// <summary>
        /// Gets mock test problem and validates configuration
        /// </summary>
        private async Task<Problem> GetAndValidateMockTestProblemAsync(Guid roundId)
        {
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
            Problem? problem = await problemRepo.Entities
                .Where(p => p.RoundId == roundId)
                .FirstOrDefaultAsync();

            if (problem == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"No problem found for round {roundId}");
            }

            if (string.IsNullOrEmpty(problem.MockTestUrl))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "This problem does not have a mock test configured");
            }

            return problem;
        }

        /// <summary>
        /// Gets student and team information for mock test submission
        /// </summary>
        private async Task<(Guid StudentId, Guid TeamId, Guid ContestId)> GetMockTestSubmitterInfoAsync(Guid roundId)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "User ID not found");

            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            Guid studentId = studentRepo.Entities
                .Where(s => s.UserId.ToString() == userId)
                .Select(s => s.StudentId)
                .FirstOrDefault();

            if (studentId == Guid.Empty)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Student not found");
            }

            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Guid contestId = contestRepo.Entities
                .Where(c => c.Rounds.Any(r => r.RoundId == roundId) && !c.DeletedAt.HasValue)
                .Select(c => c.ContestId)
                .FirstOrDefault();

            if (contestId == Guid.Empty)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Contest not found for this round");
            }

            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
            Guid teamId = teamRepo.Entities
                .Where(t => t.TeamMembers.Any(tm => tm.StudentId == studentId)
                    && !t.DeletedAt.HasValue
                    && t.ContestId == contestId)
                .Select(t => t.TeamId)
                .FirstOrDefault();

            if (teamId == Guid.Empty)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "You are not in a team for this contest");
            }

            return (studentId, teamId, contestId);
        }

        /// <summary>
        /// Creates mock test submission with artifact
        /// </summary>
        private async Task<Submission> CreateMockTestSubmissionAsync(
            Guid teamId,
            Guid problemId,
            Guid studentId,
            string artifactType,
            string artifactUrl)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            Submission submission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = teamId,
                ProblemId = problemId,
                SubmittedByStudentId = studentId,
                JudgedBy = DEFAULT_JUDGED_BY,
                Status = SubmissionStatusEnum.Pending.ToString(),
                Score = 0,
                CreatedAt = DateTime.UtcNow
            };

            await submissionRepo.InsertAsync(submission);

            // Save artifact
            IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
            SubmissionArtifact artifact = new SubmissionArtifact
            {
                ArtifactId = Guid.NewGuid(),
                SubmissionId = submission.SubmissionId,
                Type = artifactType,
                Url = artifactUrl,
                CreatedAt = DateTime.UtcNow
            };

            await artifactRepo.InsertAsync(artifact);
            await _unitOfWork.SaveAsync();

            return submission;
        }

        /// <summary>
        /// Handles failed mock test execution
        /// </summary>
        private async Task<JudgeSubmissionResultDTO> HandleFailedMockTestAsync(
            Submission submission,
            JudgeSubmissionResultDTO mockResult)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

            submission.Status = SubmissionStatusEnum.Finished.ToString();
            submission.Score = 0;
            await submissionRepo.UpdateAsync(submission);
            await _unitOfWork.SaveAsync();

            // Update summary to reflect failure
            mockResult.Summary.rawScore = 0;
            mockResult.Summary.penaltyScore = 0;

            return mockResult;
        }

        public async Task TransferSubmissionsToOtherJudge(Guid roundId, Guid judgeId)
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

                // Verify current user is the organizer
                string currentUserId = GetCurrentUserIdOrThrow();
                if (round.Contest.CreatedBy != currentUserId)
                {
                    throw new ErrorException(
                        StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "Only the contest organizer can transfer submissions"
                    );
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
                IList<JudgeInContestDTO> allJudges = await _contestJudgeService.GetJudgesByContestAsync(round.ContestId);

                if (allJudges == null || !allJudges.Any())
                {
                    throw new ErrorException(
                        StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "No judges available for this contest"
                    );
                }

                // Filter only active judges, excluding the transferring judge
                List<JudgeInContestDTO> activeJudges = allJudges
                    .Where(j => j.Status.ToLower() == JUDGE_STATUS_ACTIVE && j.UserId != judgeId)
                    .ToList();

                if (!activeJudges.Any())
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "No other active judges available to transfer submissions to"
                    );
                }

                // Get submissions assigned to the specified judge in this round
                List<Submission> submissionsToTransfer = await _unitOfWork.GetRepository<Submission>()
                    .Entities
                    .Include(s => s.Team)
                    .Where(s => s.Problem.RoundId == roundId
                                && !s.DeletedAt.HasValue
                                && s.JudgedBy == judgeId.ToString()
                                && s.Status == SUBMISSION_STATUS_PENDING)
                    .OrderBy(s => s.CreatedAt)
                    .ToListAsync();

                if (!submissionsToTransfer.Any())
                {
                    // No submissions to transfer, commit and return
                    _unitOfWork.CommitTransaction();
                    return;
                }

                // Distribute submissions equally using round-robin
                int judgeIndex = 0;
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Calculate default deadline
                int judgeDays = await GetContestPolicyDaysAsync(
                    round.ContestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);
                DateTime defaultJudgeDeadline = round.End.AddDays(judgeDays);

                // Assign submissions to other judges
                foreach (Submission submission in submissionsToTransfer)
                {
                    JudgeInContestDTO assignedJudge = activeJudges[judgeIndex];

                    // Retrieve old judge deadline config to preserve the deadline
                    string oldKey = ConfigKeys.JudgeSubmissionDeadline(judgeId, submission.SubmissionId);
                    Config? oldConfig = await configRepo.Entities
                        .Where(c => c.Key == oldKey && c.DeletedAt == null)
                        .FirstOrDefaultAsync();

                    // Preserve the old deadline, or use default if not found
                    DateTime judgeDeadline = defaultJudgeDeadline;
                    if (oldConfig != null && DateTime.TryParse(oldConfig.Value, out DateTime parsedDeadline))
                    {
                        judgeDeadline = parsedDeadline;
                    }

                    // Update submission's judge
                    submission.JudgedBy = assignedJudge.UserId.ToString();
                    submissionRepo.Update(submission);

                    // Soft delete old judge deadline config
                    if (oldConfig != null)
                    {
                        oldConfig.DeletedAt = DateTime.UtcNow;
                        await configRepo.UpdateAsync(oldConfig);
                    }

                    // Set new judge deadline for this submission
                    string newKey = ConfigKeys.JudgeSubmissionDeadline(assignedJudge.UserId, submission.SubmissionId);
                    Config? existing = await configRepo.Entities
                        .Where(c => c.Key == newKey && c.DeletedAt == null)
                        .FirstOrDefaultAsync();

                    if (existing == null)
                    {
                        await configRepo.InsertAsync(new Config
                        {
                            Key = newKey,
                            Value = judgeDeadline.ToString("o"),
                            Scope = SCOPE_CONTEST,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.Value = judgeDeadline.ToString("o");
                        existing.Scope = SCOPE_CONTEST;
                        existing.UpdatedAt = DateTime.UtcNow;
                        await configRepo.UpdateAsync(existing);
                    }

                    // Move to next judge
                    judgeIndex = (judgeIndex + 1) % activeJudges.Count;
                }

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
                    $"Error transferring submissions: {ex.Message}"
                );
            }
        }
    }
}
