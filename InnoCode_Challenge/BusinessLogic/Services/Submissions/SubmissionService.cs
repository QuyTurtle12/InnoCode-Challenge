using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.PlagiarismDTOs;
using Repository.DTOs.PlagiarismDTOs.Repository.DTOs.PlagiarismDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.DTOs.SubmissionDetailDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using System;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Security.Claims;
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

        private const string OPERATION_NAME = "submit code";
        private const string DEFAULT_JUDGED_BY = "system";
        private const double DEFAULT_TIMELIMIT = 10.0;
        private const int DEFAULT_MEMORY = 512000;
        private const string FILE_ARTIFACT_TYPE = "file";
        private const string CODE_ARTIFACT_TYPE = "code";
        private const string AUTO_TEST_SUBMISSION_FOLDER = "code-submissions";
        private const string MANUAL_TEST_SUBMISSION_FOLDER = "submissions";

        private const string STATUS_PLAGIARISM_SUSPECTED = "PlagiarismSuspected";
        private const string STATUS_PLAGIARISM_CONFIRMED = "PlagiarismConfirmed";
        private const string FP_ALGORITHM = "sha256_py_v1";
        private const int MIN_NORMALIZED_LEN_TO_CHECK = 120;

        // Constructor
        public SubmissionService(
            IUOW unitOfWork,
            IMapper mapper,
            IJudge0Service judge0Service,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            ILeaderboardEntryService leaderboardService,
            IConfigService configService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _judge0Service = judge0Service;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _leaderboardService = leaderboardService;
            _configService = configService;
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
                await ValidateRoundDeadlineAsync(roundId, "submit code");

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

        public async Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(Guid roundId, CreateSubmissionDTO submissionDTO, TestCaseEvaluationTypeEnum evaluationType)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Check round deadline before allowing submission
                await ValidateRoundDeadlineAsync(roundId, OPERATION_NAME);

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

                bool IsAlreadyFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

                if (IsAlreadyFinishedRound)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        $"Cannot execute code. You have already finished this round.");
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

                // Count previous submissions of logged-in student for this problem
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                int previousSubmissionsCount = await submissionRepo.Entities
                    .Where(s => s.ProblemId == problem.ProblemId &&
                           s.SubmittedByStudentId == studentId
                           && s.DeletedAt == null)
                    .CountAsync();

                // Determine the source code and artifact details based on evaluation type
                string sourceCode;
                string artifactType;
                string artifactUrl;

                if (evaluationType == TestCaseEvaluationTypeEnum.File)
                {
                    // For file-based evaluation, upload file to Cloudinary and get URL
                    if (submissionDTO.File == null || submissionDTO.File.Length == 0)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "File is required for File evaluation type");
                    }

                    // Validate file type (Python files only)
                    string fileExtension = Path.GetExtension(submissionDTO.File.FileName).ToLower();
                    List<string> allowedExtensions = new List<string> { ".py", ".python" };

                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            $"File type {fileExtension} is not supported. Allowed types: {string.Join(", ", allowedExtensions)}");
                    }

                    // Upload file to Cloudinary
                    artifactUrl = await _cloudinaryService.UploadFileAsync(submissionDTO.File, AUTO_TEST_SUBMISSION_FOLDER);

                    // Download file content from Cloudinary URL for Judge0 execution
                    sourceCode = await SubmissionHelpers.DownloadFileContentAsync(artifactUrl);
                    artifactType = FILE_ARTIFACT_TYPE;
                }
                else
                {
                    // For code-based evaluation, convert code to file and upload to Cloudinary
                    if (string.IsNullOrEmpty(submissionDTO.Code))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Code is required for Code evaluation type");
                    }

                    // Unescape the code to convert \n, \t,... to actual characters
                    sourceCode = System.Text.RegularExpressions.Regex.Unescape(submissionDTO.Code);

                    // Upload code directly to Cloudinary as a text file
                    string fileName = $"code_{studentId}_{DateTime.UtcNow:yyyyMMddHHmmss}.py";
                    artifactUrl = await UploadCodeAsFileAsync(submissionDTO.Code, fileName);
                    artifactType = CODE_ARTIFACT_TYPE;
                }

                // Create a submission record
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = teamId,
                    ProblemId = problem.ProblemId,
                    SubmittedByStudentId = studentId,
                    JudgedBy = DEFAULT_JUDGED_BY,
                    Status = SubmissionStatusEnum.Pending.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                await submissionRepo.InsertAsync(submission);

                // Save submission artifact
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

                // Convert to Judge0 request format
                JudgeSubmissionRequestDTO judge0Request = new JudgeSubmissionRequestDTO
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

                // Auto evaluate submission using Judge0 service
                JudgeSubmissionResultDTO result = await _judge0Service.AutoEvaluateSubmissionAsync(judge0Request);

                // Set submission ID in result
                result.SubmissionId = submission.SubmissionId.ToString();

                // Save results with penalty applied
                await SaveSubmissionResultAsync(submission.SubmissionId, result, previousSubmissionsCount, problem.PenaltyRate);
                var (suspected, matchedSubmissionId) =
                    await CheckAndFlagPlagiarismAsync(submission, problem, sourceCode);

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
                        $"Cannot {OPERATION_NAME}. You have already finished this round.");
                }

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

                // If there are previous submissions, attempt to delete their uploaded files and mark artifacts deleted
                if (previousSubmissions != null && previousSubmissions.Any())
                {
                    IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();

                    List<Guid> prevSubmissionIds = previousSubmissions.Select(s => s.SubmissionId).ToList();

                    List<SubmissionArtifact> previousArtifacts = await artifactRepo.Entities
                        .Where(a => prevSubmissionIds.Contains(a.SubmissionId) && a.Type == "file" && a.DeletedAt == null)
                        .ToListAsync();

                    foreach (SubmissionArtifact art in previousArtifacts)
                    {
                        try
                        {
                            string? publicId = CloudinaryHelpers.ExtractCloudinaryPublicId(art.Url);
                            if (!string.IsNullOrWhiteSpace(publicId))
                            {
                                // Attempt to delete remote file; failures are logged but do not abort operation
                                try
                                {
                                    await _cloudinaryService.DeleteFileAsync(publicId);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to delete old submission file from Cloudinary (publicId={publicId}): {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to extract/delete previous artifact: {ex.Message}");
                        }

                        // Mark artifact as deleted
                        art.DeletedAt = DateTime.UtcNow;
                        await artifactRepo.UpdateAsync(art);
                    }

                    // Mark previous submissions as deleted
                    foreach (Submission item in previousSubmissions)
                    {
                        item.DeletedAt = DateTime.UtcNow;
                        await submissionRepo.UpdateAsync(item);
                    }

                    await _unitOfWork.SaveAsync();
                }

                // Upload file to Cloudinary
                string fileUrl = await _cloudinaryService.UploadFileAsync(file, MANUAL_TEST_SUBMISSION_FOLDER);

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
                    JudgedBy = null,
                    Status = SubmissionStatusEnum.Pending.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                await submissionRepo.InsertAsync(submission);

                // Save submission artifact
                IGenericRepository<SubmissionArtifact> newArtifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                SubmissionArtifact artifact = new SubmissionArtifact
                {
                    ArtifactId = Guid.NewGuid(),
                    SubmissionId = submission.SubmissionId,
                    Type = "file",
                    Url = fileUrl,
                    CreatedAt = DateTime.UtcNow
                };

                await newArtifactRepo.InsertAsync(artifact);

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

                // Update team score in leaderboard
                await _leaderboardService.UpdateTeamScoreAsync(contestId, submission.TeamId);

                // Mark round as finished for this student
                await _configService.MarkFinishedSubmissionAsync(roundId, studentId);

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
                        .ThenInclude(p => p.Round)
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
                        && tc.Type == TestCaseTypeEnum.Manual.ToString()
                        && !tc.DeletedAt.HasValue)
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

                // Update submission with total score and status
                submission.Score = Math.Round(totalScore, 2);
                submission.Status = SubmissionStatusEnum.Finished.ToString();
                submission.JudgedBy = user?.UserId.ToString() ?? "Unknown Judge Id";

                // Update submission record
                await submissionRepo.UpdateAsync(submission);

                await _unitOfWork.SaveAsync();

                // Update leaderboard
                Guid contestId = submission.Problem.Round.ContestId;
                try
                {
                    // Update team score in leaderboard
                    await _leaderboardService.UpdateTeamScoreAsync(contestId, submission.TeamId);
                }
                catch (Exception ex)
                {
                    // Log the error but do not fail the entire operation
                    Console.WriteLine($"Failed to update leaderboard: {ex.Message}");
                }

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return new RubricEvaluationResultDTO
                {
                    SubmissionId = submissionId,
                    JudgedBy = user?.Email ?? "Unknown",
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
                        MaxScore = d.Testcase?.Weight ?? 0,
                        Score = d.Weight ?? 0,
                        Note = d.Note
                    })
                    .ToList();

                //// Get judge email
                //IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
                //string? judgeEmail = await userRepo.Entities
                //    .Where(u => u.UserId == Guid.Parse(submission.JudgedBy!))
                //    .Select(u => u.Email)
                //    .FirstOrDefaultAsync() ?? submission.JudgedBy;

                RubricEvaluationResultDTO result = new RubricEvaluationResultDTO
                {
                    SubmissionId = submission.SubmissionId,
                    StudentName = submission.SubmittedByStudent?.User.Fullname ?? "Unknown",
                    TeamName = submission.Team?.Name ?? "Unknown",
                    SubmittedAt = submission.CreatedAt,
                    //JudgedBy = judgeEmail,
                    JudgedBy = "unknown judge",
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
                        && s.Problem.Type == ProblemTypeEnum.AutoEvaluation.ToString()
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
                        && s.Problem.Type == ProblemTypeEnum.AutoEvaluation.ToString()
                        && !s.DeletedAt.HasValue)
                    .CountAsync();

                // Map to DTO
                GetSubmissionDTO dto = _mapper.Map<GetSubmissionDTO>(submission);
                dto.TeamName = submission!.Team?.Name ?? string.Empty;
                dto.SubmittedByStudentName = submission.SubmittedByStudent?.User.Fullname ?? string.Empty;
                dto.submissionAttemptNumber = attemptNumber;

                // Map test case details to DTOs
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
                        && s.Problem.Type == ProblemTypeEnum.AutoEvaluation.ToString()
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
                    dto.Details = submission.SubmissionDetails?
                        .Select(detail => _mapper.Map<GetSubmissionDetailDTO>(detail))
                        .ToList();

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
                                MaxScore = sd.Testcase?.Weight ?? 0,
                                Score = sd.Weight ?? 0,
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
                            MaxScore = sd.Testcase?.Weight ?? 0,
                            Score = sd.Weight ?? 0,
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
                .OrderByDescending(x => x.Submission.CreatedAt)
                .Select(x => new PlagiarismMatchDTO
                {
                    SubmissionId = x.SubmissionId,
                    TeamId = x.TeamId,
                    TeamName = x.Submission.Team.Name,
                    StudentId = x.Submission.SubmittedByStudentId,
                    StudentName = x.Submission.SubmittedByStudent.User.Fullname,
                    SubmittedAt = x.Submission.CreatedAt,
                    Score = x.Submission.Score
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

        public async Task ResolvePlagiarismSubmissionAsync(Guid submissionId, ResolvePlagiarismDTO dto)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                var submissionRepo = _unitOfWork.GetRepository<Submission>();

                Submission? submission = await submissionRepo.Entities
                    .Where(s => s.SubmissionId == submissionId && s.DeletedAt == null)
                    .Include(s => s.Problem)
                        .ThenInclude(p => p.Round)
                    .FirstOrDefaultAsync();

                if (submission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission {submissionId} not found");
                }

                if (!string.Equals(submission.Status, STATUS_PLAGIARISM_SUSPECTED, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Submission is not in plagiarism suspected state.");
                }

                // staff/admin userId
                string staffUserId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "User ID not found");

                submission.JudgedBy = staffUserId;

                if (dto.Resolution == PlagiarismResolutionEnum.Cleared)
                {
                    submission.Status = SubmissionStatusEnum.Finished.ToString();
                }
                else 
                {
                    submission.Status = STATUS_PLAGIARISM_CONFIRMED;
                    submission.Score = 0;
                }

                await submissionRepo.UpdateAsync(submission);
                await _unitOfWork.SaveAsync();

                Guid roundId = submission.Problem.RoundId;
                Guid studentId = submission.SubmittedByStudentId;
                Guid contestId = submission.Problem.Round.ContestId;

                await _configService.MarkFinishedSubmissionAsync(roundId, studentId);
                if (dto.Resolution == PlagiarismResolutionEnum.Cleared && dto.ApplyLeaderboard)
                {
                    await _leaderboardService.UpdateTeamScoreAsync(contestId, submission.TeamId);
                }

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
            // Normalize -> hash
            string normalized = PlagiarismHelpers.NormalizePython(sourceCode, removeTripleQuoted: true);

            // Avoid false positives on tiny/template code
            if (normalized.Length < MIN_NORMALIZED_LEN_TO_CHECK)
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

            // Avoid duplicate fingerprints
            bool exists = await fpRepo.Entities.AnyAsync(x => x.SubmissionId == submission.SubmissionId);
            if (!exists)
            {
                await fpRepo.InsertAsync(fp);
            }

            // If duplicate found -> set status for staff review
            if (matchedId.HasValue)
            {
                var submissionRepo = _unitOfWork.GetRepository<Submission>();

                submission.Status = STATUS_PLAGIARISM_SUSPECTED;
                submission.JudgedBy = null; // unassigned -> staff queue
                await submissionRepo.UpdateAsync(submission);
            }

            await _unitOfWork.SaveAsync();
            return (matchedId.HasValue, matchedId);
        }

    }
}
