using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TestCaseDTOs;
using Repository.IRepositories;
using Utility.Constant;
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

        public async Task CreateTestCaseAsync(CreateTestCaseDTO TestCaseDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to entity
                TestCase testCase = _mapper.Map<TestCase>(TestCaseDTO);
                
                // Get the repository for TestCase entities
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                
                // Insert the new test case
                await testCaseRepo.InsertAsync(testCase);
                
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
                    $"Error creating Rounds: {ex.Message}");
            }
        }

        public async Task DeleteTestCaseAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for TestCase entities
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Find the test case by ID
                TestCase? testCase = await testCaseRepo
                    .Entities
                    .Where(tc => tc.TestCaseId == id)
                    .Include(tc => tc.SubmissionDetails)
                    .FirstOrDefaultAsync();

                // Check if test case exists
                if (testCase == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Test case with ID {id} not found.");
                }
                
                // Check if test case has associated submission details
                if (testCase.SubmissionDetails != null && testCase.SubmissionDetails.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Cannot delete test case with ID {id} as it has associated submission details.");
                }
                
                // Delete the test case
                await testCaseRepo.DeleteAsync(testCase);
                
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
                    $"Error deleting test case: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetTestCaseDTO>> GetPaginatedTestCaseAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? problemIdSearch)
        {
            try
            {
                // Get the repository for TestCase entities
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();
                
                // Build query with optional filters
                IQueryable<TestCase> query = testCaseRepo.Entities;
                
                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(tc => tc.TestCaseId == idSearch.Value);
                }
                
                if (problemIdSearch.HasValue)
                {
                    query = query.Where(tc => tc.ProblemId == problemIdSearch.Value);
                }
                
                // Order results for consistency
                query = query.OrderBy(tc => tc.TestCaseId);
                
                // Get paginated results
                PaginatedList<TestCase> paginatedTestCases = await testCaseRepo.GetPagingAsync(query, pageNumber, pageSize);
                
                // Map entities to DTOs
                IReadOnlyCollection<GetTestCaseDTO> testCaseDTOs = paginatedTestCases.Items
                    .Select(item => _mapper.Map<GetTestCaseDTO>(item))
                    .ToList();
                
                // Create and return the paginated list of DTOs
                return new PaginatedList<GetTestCaseDTO>(
                    testCaseDTOs, 
                    paginatedTestCases.TotalCount, 
                    paginatedTestCases.PageNumber, 
                    paginatedTestCases.PageSize
                );
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated test cases: {ex.Message}");
            }
        }

        public async Task UpdateTestCaseAsync(Guid id, UpdateTestCaseDTO TestCaseDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for TestCase entities
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Find the test case by ID
                TestCase? testCase = await testCaseRepo.GetByIdAsync(id);

                // Check if test case exists
                if (testCase == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Test case with ID {id} not found.");
                }

                // Update the test case with values from DTO
                _mapper.Map(TestCaseDTO, testCase);
                
                // Update the entity
                await testCaseRepo.UpdateAsync(testCase);
                
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
                    $"Error updating test case: {ex.Message}");
            }
        }
    }
}
