using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ProblemDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class ProblemService : IProblemService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public ProblemService(IMapper mapper, IUOW unitOfWork)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        public async Task CreateProblemAsync(Guid roundId, CreateProblemDTO problemDTO)
        {
            try
            {
                    
                // Map DTO to entity and insert
                Problem problem = _mapper.Map<Problem>(problemDTO);
                
                // Get the problem repository
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();

                // Assign the roundId
                problem.RoundId = roundId;

                // Set creation timestamp
                problem.CreatedAt = DateTime.UtcNow;

                // Insert the new problem
                await problemRepo.InsertAsync(problem);
                
                // Save changes to the database
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
                    $"Error creating Rounds: {ex.Message}");
            }
        }

        public async Task DeleteProblemAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
            
                // Get the problem repository
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
        
                // Find the problem by id
                Problem? problem = await problemRepo.GetByIdAsync(id);
        
                // Check if the problem exists
                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found");
                }
        
                // Delete the problem (soft delete)
                problem.DeletedAt = DateTime.UtcNow;

                // Save changes to the database
                await _unitOfWork.SaveAsync();
        
                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something else fails, roll back the transaction
                _unitOfWork.RollBack();
                
                if (ex is ErrorException)
                {
                    throw;
                }
                
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Problem: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetProblemDTO>> GetPaginatedProblemAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch, string? roundNameSearch)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Get the problem repository
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                
                // Create a query from the repository entities
                IQueryable<Problem> query = problemRepo
                    .Entities
                    .Include(p => p.Round);
                
                // Apply filters based on search parameters
                if (idSearch.HasValue && idSearch != Guid.Empty)
                {
                    query = query.Where(p => p.ProblemId == idSearch);
                }
                    
                if (roundIdSearch.HasValue && roundIdSearch != Guid.Empty)
                {
                    query = query.Where(p => p.RoundId == roundIdSearch);
                }
                    
                if (!string.IsNullOrWhiteSpace(roundNameSearch))
                {
                    query = query.Where(p => p.Round.Name.Contains(roundNameSearch));
                }
                
                // Exclude deleted problems
                query = query.Where(p => p.DeletedAt == null);
                
                // Order by created date (newest first)
                query = query.OrderByDescending(p => p.CreatedAt);
                
                // Get paginated results
                PaginatedList<Problem> paginatedProblems = await problemRepo.GetPagingAsync(query, pageNumber, pageSize);
                
                // Map the entities to DTOs
                IReadOnlyCollection<GetProblemDTO> problemDTOs = paginatedProblems.Items.Select(item =>
                {
                    GetProblemDTO dto = _mapper.Map<GetProblemDTO>(item);
                    
                    return dto;

                }).ToList();

                // Create a new paginated list with the mapped DTOs
                return new PaginatedList<GetProblemDTO>(
                    problemDTOs, 
                    paginatedProblems.TotalCount, 
                    paginatedProblems.PageNumber, 
                    paginatedProblems.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Problems: {ex.Message}");
            }
        }

        public async Task UpdateProblemAsync(Guid id, UpdateProblemDTO problemDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get the problem repository
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                
                // Find the problem by id
                Problem? problem = await problemRepo.GetByIdAsync(id);
                
                // Check if the problem exists
                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found");
                }

                // Update the problem properties
                _mapper.Map(problemDTO, problem);
                problem.Type = problemDTO.Type.ToString();
                
                // Update the problem
                await problemRepo.UpdateAsync(problem);
                
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
                    $"Error updating Problem: {ex.Message}");
            }
        }
    }
}
