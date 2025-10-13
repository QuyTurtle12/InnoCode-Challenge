using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Repository.DTOs.McqTestDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class McqTestService : IMcqTestService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public McqTestService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task CreateMcqTest(CreateMcqTestDTO mcqTestDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to entity
                McqTest mcqTest = _mapper.Map<McqTest>(mcqTestDTO);

                // Get mcqTest repository
                IGenericRepository<McqTest> McqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Insert new MCQ test
                await McqTestRepo.InsertAsync(mcqTest);

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
                    $"Error creating MCQ Test: {ex.Message}");
            }
        }

        public async Task DeleteMcqTest(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get mcqTest repository
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Find the MCQ test by id
                var mcqTest = await mcqTestRepo.GetByIdAsync(id);
                if (mcqTest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"MCQ Test with ID {id} not found.");
                }

                // Delete the MCQ test
                await mcqTestRepo.DeleteAsync(mcqTest);

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
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated MCQ Tests: {ex.Message}");
            }
        }

        public async Task UpdateMcqTest(Guid id, UpdateMcqTestDTO mcqTestDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get mcqTest repository
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

                // Find the MCQ test by id
                var mcqTest = await mcqTestRepo.GetByIdAsync(id);
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

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating MCQ Test: {ex.Message}");
            }
        }
    }
}
