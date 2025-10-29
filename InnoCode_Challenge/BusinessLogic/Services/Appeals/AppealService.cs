using AutoMapper;
using BusinessLogic.IServices.Appeals;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.AppealDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Appeals
{
    public class AppealService : IAppealService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public AppealService(IMapper mapper, IUOW unitOfWork)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        public async Task CreateAppealAsync(CreateAppealDTO appealDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to Entity
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

                // Map the DTO to the Entity
                Appeal appeal = _mapper.Map<Appeal>(appealDTO);

                // Insert the new entity
                await appealRepo.InsertAsync(appeal);

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
                    $"Error creating Appeal: {ex.Message}");
            }
        }

        public async Task DeleteAppealAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get the repository
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

                // Find the appeal by id
                Appeal? appeal = await appealRepo.GetByIdAsync(id);
                if (appeal == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Appeal with ID {id} not found");
                }

                // Delete the appeal (soft delete)
                appeal.DeletedAt = DateTime.UtcNow;

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Roll back the transaction and throw a new custom exception
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Appeal: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetAppealDTO>> GetPaginatedAppealAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? teamIdSearch, Guid? ownerIdSearch, string? teamNameSearch, string? ownerNameSearch)
        {
            try
            {
                // Get the repository
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

                // Start with a base query of non-deleted appeals
                IQueryable<Appeal> query = appealRepo
                    .Entities
                    .Where(a => !a.DeletedAt.HasValue)
                    .Include(a => a.Team)
                    .Include(a => a.Owner);

                // Apply search filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(a => a.AppealId == idSearch.Value);
                }

                if (teamIdSearch.HasValue)
                {
                    query = query.Where(a => a.TeamId == teamIdSearch.Value);
                }

                if (ownerIdSearch.HasValue)
                {
                    query = query.Where(a => a.OwnerId == ownerIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(teamNameSearch))
                {
                    query = query.Where(a => a.Team.Name.Contains(teamNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(ownerNameSearch))
                {
                    query = query.Where(a => a.Owner.Fullname.Contains(ownerNameSearch));
                }

                // Get paginated result
                PaginatedList<Appeal> resultQuery = await appealRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map entity to DTO
                IReadOnlyCollection<GetAppealDTO> result = resultQuery.Items.Select(item => {
                    GetAppealDTO appealDTO = _mapper.Map<GetAppealDTO>(item);

                    return appealDTO;
                }).ToList();

                // Create and return paginated list of DTOs
                return new PaginatedList<GetAppealDTO>(
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
                    $"Error retrieving paginated appeals: {ex.Message}");
            }
        }

        public async Task UpdateAppealAsync(Guid id, UpdateAppealDTO appealDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get the repository
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

                // Find the appeal by id
                Appeal? appeal = await appealRepo.GetByIdAsync(id);
                if (appeal == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Appeal with ID {id} not found");
                }

                // Update properties from DTO
                appeal.State = appealDTO.State.ToString();
                appeal.Decision = appealDTO.Decision;

                // Update the appeal
                await appealRepo.UpdateAsync(appeal);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Roll back the transaction and throw a new custom exception
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Appeal: {ex.Message}");
            }
        }
    }
}
