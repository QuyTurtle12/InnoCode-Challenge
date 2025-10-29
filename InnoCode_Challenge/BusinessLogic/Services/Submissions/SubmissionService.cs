using System.Security.Claims;
using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using Repository.ResponseModel;
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

        // Constructor
        public SubmissionService(
            IUOW unitOfWork,
            IMapper mapper,
            IJudge0Service judge0Service,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _judge0Service = judge0Service;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
        }

        public async Task CreateSubmissionAsync(CreateSubmissionDTO submissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Map DTO to entity
                Submission submission = _mapper.Map<Submission>(submissionDTO);
                
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Set creation timestamp
                submission.CreatedAt = DateTime.UtcNow;

                // Insert the new submission
                await submissionRepo.InsertAsync(submission);
                
                // Save changes to the database
                await _unitOfWork.SaveAsync();
                
                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Submission: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetSubmissionDTO>> GetPaginatedSubmissionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? problemIdSearch, Guid? SubmittedByStudentId, string? teamName, string? studentName)
        {
            try
            {
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                
                // Start with base query
                IQueryable<Submission> query = submissionRepo
                    .Entities
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User);
                
                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(s => s.SubmissionId == idSearch.Value);
                }
                
                if (problemIdSearch.HasValue)
                {
                    query = query.Where(s => s.ProblemId == problemIdSearch.Value);
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
                
                // Map to DTOs
                IReadOnlyCollection<GetSubmissionDTO> result = resultQuery.Items.Select(item =>
                {
                    GetSubmissionDTO? dto = _mapper.Map<GetSubmissionDTO>(item);

                    dto.TeamName = item.Team?.Name ?? string.Empty;

                    dto.SubmittedByStudentName = item.SubmittedByStudent?.User.Fullname!;
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
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Submission: {ex.Message}");
            }
        }

        public async Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(CreateSubmissionDTO submissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get problem info
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                Problem? problem = await problemRepo.GetByIdAsync(submissionDTO.ProblemId);

                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Problem with ID {submissionDTO.ProblemId} not found");
                }

                // Get test cases for the problem
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                IList<TestCase> testCases = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == submissionDTO.ProblemId)
                    .ToList();

                if (!testCases.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"No test cases found for problem {submissionDTO.ProblemId}");
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

                // Count previous submissions for this problem by this student's team
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                int previousSubmissionsCount = await submissionRepo.Entities
                    .Where(s => s.ProblemId == submissionDTO.ProblemId &&
                           s.TeamId == submissionDTO.TeamId)
                    .CountAsync();

                // Create a submission record
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = submissionDTO.TeamId,
                    ProblemId = submissionDTO.ProblemId,
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
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error evaluating submission: {ex.Message}");
            }
        }

        public async Task SaveSubmissionResultAsync(Guid submissionId, JudgeSubmissionResultDTO result,
    int previousSubmissionsCount = 0, double? penaltyRate = null)
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
                        Weight = (int)testCase.Weight,
                        Note = caseResult.Status,
                        RuntimeMs = SubmissionHelpers.ParseRuntime(caseResult.Time),
                        MemoryKb = caseResult.MemoryKb,
                        CreatedAt = DateTime.UtcNow
                    };

                    await submissionDetailRepo.InsertAsync(detail);
                }

                // Calculate raw score (before penalty)
                double rawScore = totalWeight > 0 ? (passedWeight / totalWeight) * 100 : 0;

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
                submission.Score = finalScore;

                // Update result summary
                result.Summary.rawScore = rawScore;
                result.Summary.penaltyScore = finalScore;

                // Update the submission record
                await submissionRepo.UpdateAsync(submission);

                // Save all changes
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error saving submission results: {ex.Message}");
            }
        }

        public async Task<Guid> CreateFileSubmissionAsync(CreateFileSubmissionDTO submissionDTO, IFormFile file)
        {
            try
            {
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

                // Begin transaction
                _unitOfWork.BeginTransaction();

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

                // Upload file to Cloudinary
                string fileUrl = await _cloudinaryService.UploadFileAsync(file, "submissions");

                // Create a submission record
                Submission submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = submissionDTO.TeamId,
                    ProblemId = submissionDTO.ProblemId,
                    SubmittedByStudentId = studentId,
                    JudgedBy = "pending",
                    Status = SubmissionStatusEnum.Pending.ToString(),
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
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
                var submission = await submissionRepo.GetByIdAsync(submissionId);

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
                    Weight = (int)score,
                    Note = feedback,
                    RuntimeMs = 0, // Not applicable
                    MemoryKb = 0, // Not applicable
                    CreatedAt = DateTime.UtcNow
                };

                await detailRepo.InsertAsync(detail);
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return true;
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating file submission score: {ex.Message}");
            }
        }

    }
}
