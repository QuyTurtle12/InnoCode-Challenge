using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class RoundService : IRoundService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        public RoundService(IMapper mapper, IUOW unitOfWork)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        public async Task CreateRoundAsync(CreateRoundDTO roundDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (roundDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest data cannot be null.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(roundDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest name is required.");
                }

                // Validate date range
                if (roundDTO.Start > roundDTO.End)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Map DTO to Entity
                Round round = _mapper.Map<Round>(roundDTO);

                // Insert new round
                await roundRepo.InsertAsync(round);

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
                    $"Error creating Rounds: {ex.Message}");
            }
        }

        public async Task DeleteRoundAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Find round by id
                Round? round = await roundRepo.GetByIdAsync(id);

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");
                }

                // Delete the round
                await roundRepo.DeleteAsync(round);

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
                    $"Error deleting Rounds: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? roundNameSearch, string? contestNameSearch, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Get all rounds
                IQueryable<Round> query = roundRepo.Entities.Include(r => r.Contest);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(r => r.RoundId == idSearch.Value);
                }

                if (contestIdSearch.HasValue)
                {
                    query = query.Where(r => r.ContestId == contestIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(roundNameSearch))
                {
                    query = query.Where(r => r.Name.Contains(roundNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(contestNameSearch))
                {
                    query = query.Where(r => r.Contest.Name.Contains(contestNameSearch));
                }

                if (startDate.HasValue)
                {
                    query = query.Where(r => r.Start >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(r => r.End <= endDate.Value);
                }

                // Change to paginated list to facilitate mapping process
                PaginatedList<Round> resultQuery = await roundRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map entities to DTOs
                IReadOnlyCollection<GetRoundDTO> result = resultQuery.Items.Select(item =>
                {
                    GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(item);

                    roundDTO.ContestName = item.Contest?.Name ?? "N/A";
                    roundDTO.RoundName = item.Name;

                    return roundDTO;
                }).ToList();

                // Create new paginated list with DTOs
                return new PaginatedList<GetRoundDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize
                );
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Rounds: {ex.Message}");
            }
        }

        public async Task UpdateRoundAsync(Guid id, UpdateRoundDTO roundDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");
                }

                if (roundDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round data cannot be null.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(roundDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round name is required.");
                }

                // Validate date range
                if (roundDTO.Start > roundDTO.End)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
                }

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Find round by id
                Round? round = await roundRepo.GetByIdAsync(id);

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");
                }

                // Update round properties
                _mapper.Map(roundDTO, round);

                // Update the round
                await roundRepo.UpdateAsync(round);

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
                    $"Error updating Rounds: {ex.Message}");
            }
        }
    }
}
