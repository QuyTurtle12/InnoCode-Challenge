using AutoMapper;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.McqAttemptItemDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Mcqs
{
    public class McqAttemptItemService : IMcqAttemptItemService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public McqAttemptItemService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task CreateMcqAttemptItemAsync(CreateMcqAttemptItemDTO mcqAttemptItemDTO)
        {
            try
            {
                // Start a new transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for McqAttemptItem
                IGenericRepository<McqAttemptItem> mcqAttemptItemRepository = _unitOfWork.GetRepository<McqAttemptItem>();

                // Map DTO to entity
                McqAttemptItem mcqAttemptItem = _mapper.Map<McqAttemptItem>(mcqAttemptItemDTO);

                // Insert the new MCQ Attempt Item
                await mcqAttemptItemRepository.InsertAsync(mcqAttemptItem);

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction if all operations succeed
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
                    $"Error creating MCQ Attempt: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetMcqAttemptItemDTO>> GetPaginatedMcqAttemptItemAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? testIdSearch, Guid? questionIdSearch, string? testName, string? questionText)
        {
            try
            {
                // Get the repository for McqAttemptItem
                IGenericRepository<McqAttemptItem> mcqAttemptItemRepo = _unitOfWork.GetRepository<McqAttemptItem>();

                // Create a query data
                IQueryable<McqAttemptItem> query = mcqAttemptItemRepo
                    .Entities
                    .Include(i => i.Question)
                    .Include(i => i.SelectedOption)
                    .Include(i => i.Test);

                // Apply filters if parameters are provided
                if (idSearch.HasValue)
                    query = query.Where(item => item.ItemId == idSearch.Value);

                if (testIdSearch.HasValue)
                    query = query.Where(item => item.TestId == testIdSearch.Value);

                if (questionIdSearch.HasValue)
                    query = query.Where(item => item.QuestionId == questionIdSearch.Value);

                // String filters with case-insensitive contains
                if (!string.IsNullOrWhiteSpace(testName))
                    query = query.Where(item => item.Test.Name != null && item.Test.Name.ToLower().Contains(testName.ToLower()));

                if (!string.IsNullOrWhiteSpace(questionText))
                    query = query.Where(item => item.Question.Text != null && item.Question.Text.ToLower().Contains(questionText.ToLower()));

                // Get paginated data
                PaginatedList<McqAttemptItem> resultQuery = await mcqAttemptItemRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map entities to DTOs
                IReadOnlyCollection<GetMcqAttemptItemDTO> result = resultQuery.Items.Select(item => 
                {
                    GetMcqAttemptItemDTO mcqAttemptItemDTO = _mapper.Map<GetMcqAttemptItemDTO>(item);

                    mcqAttemptItemDTO.QuestionText = item.Question.Text;
                    mcqAttemptItemDTO.TestName = item.Test?.Name ?? string.Empty;
                    mcqAttemptItemDTO.OptionText = item.SelectedOption?.Text ?? string.Empty;

                    return mcqAttemptItemDTO;
                }).ToList();

                // Create a new paginated list with the mapped items
                return new PaginatedList<GetMcqAttemptItemDTO>(
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
                    $"Error retrieving MCQ Attempt Items: {ex.Message}");
            }
        }
    }
}
