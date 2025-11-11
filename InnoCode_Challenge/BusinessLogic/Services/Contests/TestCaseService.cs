using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TestCaseDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class TestCaseService : ITestCaseService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public TestCaseService(IMapper mapper, IUOW unitOfWork)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        public async Task CreateTestCaseAsync(Guid roundId, CreateTestCaseDTO testCaseDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (testCaseDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test case data cannot be null.");
                }

                // Validate round and get problem
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.Problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found for this round.");
                }

                // Validate problem type
                if (round.Problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test cases can only be created for AutoEvaluation problems.");
                }

                // Validate test case type
                if (testCaseDTO.Type != TestCaseTypeEnum.TestCase)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Only TestCase type is allowed.");
                }

                // Get TestCase Repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Map DTO to Entity
                TestCase testCase = _mapper.Map<TestCase>(testCaseDTO);
                testCase.TestCaseId = Guid.NewGuid();
                testCase.ProblemId = round.Problem.ProblemId;
                testCase.Type = TestCaseTypeEnum.TestCase.ToString();

                // Insert new test case
                await testCaseRepo.InsertAsync(testCase);

                // Save changes
                await _unitOfWork.SaveAsync();

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
                    $"Error creating test case: {ex.Message}");
            }
        }

        public async Task BulkUpdateTestCasesAsync(Guid roundId, IList<BulkUpdateTestCaseDTO> testCaseDTOs)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (testCaseDTOs == null || !testCaseDTOs.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test case list cannot be null or empty.");
                }

                // Validate round and get problem with test cases
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .Include(r => r.Problem)
                        .ThenInclude(p => p!.TestCases)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.Problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found for this round.");
                }

                // Validate problem type
                if (round.Problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test cases can only be updated for AutoEvaluation problems.");
                }

                // Get TestCase Repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Get all test case IDs from DTOs
                List<Guid> testCaseIds = testCaseDTOs.Select(dto => dto.TestCaseId).ToList();

                // Check for duplicates in the request
                if (testCaseIds.Count != testCaseIds.Distinct().Count())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Duplicate test case IDs found in the request.");
                }

                // Fetch all existing test cases that match the IDs and belong to this problem
                List<TestCase> existingTestCases = await testCaseRepo.Entities
                    .Where(tc => testCaseIds.Contains(tc.TestCaseId)
                        && tc.ProblemId == round.Problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.TestCase.ToString())
                    .ToListAsync();

                // Validate all test cases exist
                if (existingTestCases.Count != testCaseIds.Count)
                {
                    List<Guid> foundIds = existingTestCases.Select(tc => tc.TestCaseId).ToList();
                    List<Guid> notFoundIds = testCaseIds.Except(foundIds).ToList();

                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"The following test case IDs were not found or do not belong to this round: {string.Join(", ", notFoundIds)}");
                }

                // Create a dictionary for quick lookup
                Dictionary<Guid, TestCase> testCaseDict = existingTestCases.ToDictionary(tc => tc.TestCaseId);

                // Update each test case
                foreach (BulkUpdateTestCaseDTO dto in testCaseDTOs)
                {
                    TestCase testCase = testCaseDict[dto.TestCaseId];

                    // Map properties from DTO to entity
                    testCase.Description = dto.Description;
                    testCase.Weight = dto.Weight;
                    testCase.TimeLimitMs = dto.TimeLimitMs;
                    testCase.MemoryKb = dto.MemoryKb;
                    testCase.Input = dto.Input;
                    testCase.ExpectedOutput = dto.ExpectedOutput;

                    // Update the test case
                    await testCaseRepo.UpdateAsync(testCase);
                }

                // Save all changes
                await _unitOfWork.SaveAsync();

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
                    $"Error bulk updating test cases: {ex.Message}");
            }
        }

        public async Task DeleteTestCaseAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test case ID cannot be empty.");
                }

                // Get TestCase Repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Find test case by id
                TestCase? testCase = await testCaseRepo.Entities
                    .Where(tc => tc.TestCaseId == id)
                    .FirstOrDefaultAsync();

                // Check if test case is not exists and the related problem is already deleted
                if (testCase == null || testCase.Problem.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Test case not found.");
                }

                // Validate that the test case type is TestCase (not Manual)
                if (testCase.Type != TestCaseTypeEnum.TestCase.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Only test cases of type 'TestCase' can be deleted.");
                }

                // Delete the test case
                testCase.DeleteAt = DateTime.UtcNow;

                await testCaseRepo.UpdateAsync(testCase);

                // Save changes
                await _unitOfWork.SaveAsync();

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
                    $"Error deleting test case: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetTestCaseDTO>> GetTestCasesByRoundIdAsync(Guid roundId, int pageNumber, int pageSize)
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

                // Validate round exists
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.Problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found for this round.");
                }

                // Validate problem type is AutoEvaluation
                if (round.Problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test cases are only available for AutoEvaluation problems.");
                }

                // Get test case repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Build query for test cases
                IQueryable<TestCase> query = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == round.Problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.TestCase.ToString())
                    .OrderBy(tc => tc.TestCaseId);

                // Get paginated results
                PaginatedList<TestCase> paginatedTestCases = await testCaseRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map to DTOs
                IReadOnlyCollection<GetTestCaseDTO> testCaseDTOs = paginatedTestCases.Items
                    .Select(tc => _mapper.Map<GetTestCaseDTO>(tc))
                    .ToList();

                // Return paginated list with DTOs
                return new PaginatedList<GetTestCaseDTO>(
                    testCaseDTOs,
                    paginatedTestCases.TotalCount,
                    paginatedTestCases.PageNumber,
                    paginatedTestCases.PageSize
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
                    $"Error retrieving test cases: {ex.Message}");
            }
        }
    }
}
