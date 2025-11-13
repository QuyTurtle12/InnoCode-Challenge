using AutoMapper;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.McqTestQuestionDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Mcqs
{
    public class McqTestService : IMcqTestService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private const double DEFAULT_WEIGHT = 1.0;

        // Constructor
        public McqTestService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task AddQuestionsToTestAsync(Guid testId, Guid bankId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get Mcq Test repository
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Get Bank repository
                IGenericRepository<Bank> bankRepo = _unitOfWork.GetRepository<Bank>();

                // Get Mcq Test Question repository
                IGenericRepository<McqTestQuestion> mcqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();


                // Get the Mcq Test
                McqTest? existingMcqTest = await mcqTestRepo
                    .Entities
                    .Where(t => t.TestId == testId)
                        .Include(t => t.McqTestQuestions)
                    .FirstOrDefaultAsync();

                // Validate test exists
                if (existingMcqTest == null) { 
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"MCQ Test with ID {testId} not found.");
                }

                // Clear existing questions from the test
                if (existingMcqTest.McqTestQuestions != null && existingMcqTest.McqTestQuestions.Any())
                {
                    // Get all existing questions for the test
                    List<McqTestQuestion> questionsToDelete = existingMcqTest.McqTestQuestions.ToList();

                    // Delete existing questions
                    foreach (McqTestQuestion question in questionsToDelete)
                    {
                        mcqTestQuestionRepo.Delete(question);
                    }

                    // Save changes after deletions
                    await _unitOfWork.SaveAsync();
                }

                // Retrieve all questions from the specified bank
                Bank? bank = await bankRepo.Entities
                    .Where(b => b.BankId == bankId)
                    .Include(b => b.McqQuestions)
                    .FirstOrDefaultAsync();

                // Validate bank exists
                if (bank == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Bank with ID {bankId} not found.");
                }

                // Validate bank has questions
                if (bank.McqQuestions == null || !bank.McqQuestions.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Bank {bankId} has no questions to add to the test.");
                }

                // Create McqTestQuestion entries for each question in the bank with sequential ordering
                List<McqTestQuestion> testQuestions = bank.McqQuestions
                    .Select((question, index) => new McqTestQuestion
                    {
                        TestId = testId,
                        QuestionId = question.QuestionId,
                        Weight = DEFAULT_WEIGHT,
                        OrderIndex = index + 1
                    })
                    .ToList();

                // Insert all test questions
                await mcqTestQuestionRepo.InsertRangeAsync(testQuestions);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                     ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                     $"Error creating test questions: {ex.Message}"
                );
            }
        }

        public async Task CreateMcqTestAsync(Guid roundId, CreateMcqTestDTO mcqTestDTO)
        {
            try
            {
                // Map DTO to entity
                McqTest mcqTest = _mapper.Map<McqTest>(mcqTestDTO);

                // Assign roundId
                mcqTest.RoundId = roundId;

                // Get mcqTest repository
                IGenericRepository<McqTest> McqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Insert new MCQ test
                await McqTestRepo.InsertAsync(mcqTest);

                // Save changes
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
                    $"Error creating MCQ Test: {ex.Message}");
            }
        }

        public async Task DeleteMcqTestAsync(Guid id)
        {
            try
            {
                // Get repositories
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();
                IGenericRepository<McqTestQuestion> mcqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();

                // Find the MCQ test by id with related entities
                McqTest? mcqTest = await mcqTestRepo.Entities
                    .Where(t => t.TestId == id)
                    .Include(t => t.McqTestQuestions)
                    .FirstOrDefaultAsync();

                // Validate MCQ test exists
                if (mcqTest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"MCQ Test with ID {id} not found.");
                }

                // Delete all related test questions
                if (mcqTest.McqTestQuestions != null && mcqTest.McqTestQuestions.Any())
                {
                    foreach (McqTestQuestion testQuestion in mcqTest.McqTestQuestions)
                    {
                        mcqTestQuestionRepo.Delete(testQuestion);
                    }
                }

                // Delete the MCQ test
                await mcqTestRepo.DeleteAsync(mcqTest);

                // Save changes
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
                    $"Error deleting MCQ Test: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetMcqTestDTO>> GetPaginatedMcqTestAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch)
        {
            try
            {
                // Get mcqTest repository
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Create query
                IQueryable<McqTest> query = mcqTestRepo.Entities;

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(x => x.TestId == idSearch.Value);
                }

                if (roundIdSearch.HasValue)
                {
                    query = query.Where(x => x.RoundId == roundIdSearch.Value);
                }

                // Get paginated list to facilitate mapping to DTOs
                PaginatedList<McqTest> paginatedMcqTests = await mcqTestRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map to DTOs
                IReadOnlyCollection<GetMcqTestDTO> mcqTestDTOs = paginatedMcqTests.Items.Select(item => {
                    GetMcqTestDTO mcqTestDTO = _mapper.Map<GetMcqTestDTO>(item);

                    return mcqTestDTO;
                }).ToList();

                // Create paginated list of DTOs
                return new PaginatedList<GetMcqTestDTO>(
                    mcqTestDTOs,
                    paginatedMcqTests.TotalCount,
                    paginatedMcqTests.PageNumber,
                    paginatedMcqTests.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }
                
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated MCQ Tests: {ex.Message}");
            }
        }

        public async Task UpdateMcqTestAsync(Guid id, UpdateMcqTestDTO mcqTestDTO)
        {
            try
            {
                // Get mcqTest repository
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Find the MCQ test by id
                McqTest? mcqTest = await mcqTestRepo.GetByIdAsync(id);
                if (mcqTest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"MCQ Test with ID {id} not found.");
                }

                // Update entity with values from DTO
                _mapper.Map(mcqTestDTO, mcqTest);
                
                // Update the MCQ test
                await mcqTestRepo.UpdateAsync(mcqTest);

                // Save changes
                await _unitOfWork.SaveAsync();
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
                    $"Error updating MCQ Test: {ex.Message}");
            }
        }

        public async Task BulkUpdateQuestionWeightsAsync(Guid testId, BulkUpdateQuestionWeightsDTO dto)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (dto == null || dto.Questions == null || !dto.Questions.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Question weights list cannot be empty.");
                }

                // Get McqTestQuestion repository
                IGenericRepository<McqTestQuestion> mcqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();

                // Get all question IDs from the request
                List<Guid> questionIds = dto.Questions.Select(q => q.QuestionId).ToList();

                // Fetch all existing test questions for this test and the specified questions
                List<McqTestQuestion> existingTestQuestions = await mcqTestQuestionRepo.Entities
                    .Where(tq => tq.TestId == testId && questionIds.Contains(tq.QuestionId))
                    .ToListAsync();

                // Check if all questions exist in the test
                if (existingTestQuestions.Count != dto.Questions.Count)
                {
                    // Find which questions are missing
                    List<Guid> foundQuestionIds = existingTestQuestions.Select(tq => tq.QuestionId).ToList();
                    List<Guid> missingQuestionIds = questionIds.Except(foundQuestionIds).ToList();

                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"The following question(s) not found in test {testId}: {string.Join(", ", missingQuestionIds)}");
                }

                // Create a dictionary for quick lookup of new weights
                Dictionary<Guid, double> weightUpdates = dto.Questions.ToDictionary(q => q.QuestionId, q => q.Weight);

                // Update each test question's weight
                foreach (McqTestQuestion testQuestion in existingTestQuestions)
                {
                    if (weightUpdates.TryGetValue(testQuestion.QuestionId, out double newWeight))
                    {
                        testQuestion.Weight = newWeight;
                        await mcqTestQuestionRepo.UpdateAsync(testQuestion);
                    }
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
                    $"Error bulk updating question weights: {ex.Message}");
            }
        }
    }
}
