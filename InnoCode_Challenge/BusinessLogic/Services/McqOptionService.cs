using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Repository.DTOs.McqOptionDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class McqOptionService : IMcqOptionService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public McqOptionService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task CreateMcqOption(CreateMcqOptionDTO mcqOptionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to entity
                McqOption mcqOption = _mapper.Map<McqOption>(mcqOptionDTO);

                // Get mcqOption repository
                IGenericRepository<McqOption> mcqOptionRepo = _unitOfWork.GetRepository<McqOption>();

                // Insert new MCQ option
                await mcqOptionRepo.InsertAsync(mcqOption);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating MCQ options: {ex.Message}");
            }
        }

        public async Task DeleteMcqOption(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get mcqOption repository
                IGenericRepository<McqOption> mcqOptionRepo = _unitOfWork.GetRepository<McqOption>();

                // Find the MCQ option by id
                var mcqOption = await mcqOptionRepo.GetByIdAsync(id);

                // Check if the option exists
                if (mcqOption == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"Option {id} not found");
                }

                await mcqOptionRepo.DeleteAsync(mcqOption);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting MCQ options: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetMcqOptionDTO>> GetPaginatedMcqOptionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? questionIdSearch)
        {
            try
            {
                // Get mcqOption repository
                IGenericRepository<McqOption> mcqOptionRepo = _unitOfWork.GetRepository<McqOption>();

                // Create base query
                IQueryable<McqOption> query = mcqOptionRepo.Entities;

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(o => o.OptionId == idSearch.Value);
                }

                if (questionIdSearch.HasValue)
                {
                    query = query.Where(o => o.QuestionId == questionIdSearch.Value);
                }

                // Order by OptionId for consistent pagination
                query = query.OrderBy(o => o.OptionId);

                // Get paginated list of entities to facilitate mapping process to DTOs
                PaginatedList<McqOption> resultQuery = await mcqOptionRepo.GetPagging(query, pageNumber, pageSize);

                // Map entities to DTOs
                IReadOnlyCollection<GetMcqOptionDTO> result = resultQuery.Items.Select(item =>
                {
                    GetMcqOptionDTO mcqOptionDTO = _mapper.Map<GetMcqOptionDTO>(item);

                    return mcqOptionDTO;
                }).ToList();

                // Create new paginated list with mapped DTOs
                return new PaginatedList<GetMcqOptionDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving MCQ options: {ex.Message}");
            }
        }

        public async Task UpdateMcqOption(Guid id, UpdateMcqOptionDTO mcqOptionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get mcqOption repository
                IGenericRepository<McqOption> mcqOptionRepo = _unitOfWork.GetRepository<McqOption>();

                // Find the MCQ option by id
                var mcqOption = await mcqOptionRepo.GetByIdAsync(id);

                // Check if the option exists
                if (mcqOption == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"Option {id} not found");
                }

                // Update the entity with data from DTO
                _mapper.Map(mcqOptionDTO, mcqOption);

                // Update the entity
                await mcqOptionRepo.UpdateAsync(mcqOption);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating MCQ option: {ex.Message}");
            }
        }
    }
}
