using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using Utility.Constant;
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

        // Constructor
        public SubmissionService(IMapper mapper, IUOW unitOfWork, IJudge0Service judge0Service)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _judge0Service = judge0Service;
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
                
                // Get paginated data
                PaginatedList<Submission> resultQuery = await submissionRepo.GetPagingAsync(query, pageNumber, pageSize);
                
                // Map to DTOs
                IReadOnlyCollection<GetSubmissionDTO> result = resultQuery.Items.Select(item => _mapper.Map<GetSubmissionDTO>(item)).ToList();
                
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
                // Get problem info
                var problemRepo = _unitOfWork.GetRepository<Problem>();
                var problem = await problemRepo.GetByIdAsync(submissionDTO.ProblemId);

                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Problem with ID {submissionDTO.ProblemId} not found");
                }

                // Get test cases for the problem
                var testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                var testCases = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == submissionDTO.ProblemId)
                    .ToList();

                if (!testCases.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"No test cases found for problem {submissionDTO.ProblemId}");
                }

                // Create a submission record
                var submission = new Submission
                {
                    SubmissionId = Guid.NewGuid(),
                    TeamId = submissionDTO.TeamId,
                    ProblemId = submissionDTO.ProblemId,
                    SubmittedByStudentId = submissionDTO.SubmittedByStudentId,
                    JudgedBy = "system",
                    Status = "Pending",
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                };

                var submissionRepo = _unitOfWork.GetRepository<Submission>();
                await submissionRepo.InsertAsync(submission);

                // Save submission artifact (the code)
                var artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                var artifact = new SubmissionArtifact
                {
                    ArtifactId = Guid.NewGuid(),
                    SubmissionId = submission.SubmissionId,
                    Type = "code",
                    Url = submissionDTO.Code, // Store code directly or save to storage service
                    CreatedAt = DateTime.UtcNow
                };

                await artifactRepo.InsertAsync(artifact);
                await _unitOfWork.SaveAsync();

                // Convert to Judge0 request format
                var judge0Request = new JudgeSubmissionRequestDTO
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
                        // You'll need to implement a way to store and retrieve test inputs/outputs
                        Stdin = tc.Input ?? string.Empty,
                        ExpectedOutput = tc.ExpectedOutput ?? string.Empty
                    }).ToList(),
                    TimeLimitSec = testCases.Max(tc => tc.TimeLimitMs) / 1000.0 ?? 2.0,
                    MemoryLimitKb = testCases.Max(tc => tc.MemoryKb) ?? 128000
                };

                // Call Judge0 service
                var result = await _judge0Service.EvaluateSubmissionAsync(judge0Request);

                // Save results
                await SaveSubmissionResultAsync(submission.SubmissionId, result);

                return result;
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error evaluating submission: {ex.Message}");
            }
        }

        public async Task SaveSubmissionResultAsync(Guid submissionId, JudgeSubmissionResultDTO result)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Update submission status
                var submissionRepo = _unitOfWork.GetRepository<Submission>();
                var submission = await submissionRepo.GetByIdAsync(submissionId);

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
                var testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                var testCases = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == submission.ProblemId)
                    .ToList();

                // Create submission details for each test case result
                var submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                foreach (var caseResult in result.Cases)
                {
                    // Find the corresponding test case
                    var testCaseGuid = Guid.Parse(caseResult.Id);
                    var testCase = testCases.FirstOrDefault(tc => tc.TestCaseId == testCaseGuid);

                    if (testCase == null) continue;

                    totalWeight += testCase.Weight;
                    if (caseResult.Status == "success")
                    {
                        passedWeight += testCase.Weight;
                    }

                    // Save submission detail
                    var detail = new SubmissionDetail
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

                // Update submission
                submission.Status = result.Summary.Failed == 0 ? "Accepted" : "PartiallyAccepted";
                submission.Score = totalWeight > 0 ? (passedWeight / totalWeight) * 100 : 0;

                await submissionRepo.UpdateAsync(submission);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error saving submission results: {ex.Message}");
            }
        }

    }
}
