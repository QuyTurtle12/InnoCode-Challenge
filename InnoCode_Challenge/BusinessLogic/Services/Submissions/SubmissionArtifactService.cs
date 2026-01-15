using AutoMapper;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Submissions
{
    public class SubmissionArtifactService : ISubmissionArtifactService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        // Constructor
        public SubmissionArtifactService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task CreateSubmissionArtifactAsync(CreateSubmissionArtifactDTO submissionArtifactDTO)
        {
            try
            {
                // Convert DTO to entity
                SubmissionArtifact submissionArtifact = _mapper.Map<SubmissionArtifact>(submissionArtifactDTO);
                
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get the repository
                IGenericRepository<SubmissionArtifact> submissionArtifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();

                // Set creation timestamp
                submissionArtifact.CreatedAt = DateTime.UtcNow;

                // Insert the new submission artifact
                await submissionArtifactRepo.InsertAsync(submissionArtifact);
                
                // Save changes
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
                    $"Error creating Submission Artifact: {ex.Message}");
            }
        }

        public async Task DeleteSubmissionArtifactAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get the repository
                IGenericRepository<SubmissionArtifact> submissionArtifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                
                // Find the artifact by id
                SubmissionArtifact? submissionArtifact = await submissionArtifactRepo.GetByIdAsync(id);
                
                // Check if artifact exists
                if (submissionArtifact == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission Artifact with ID {id} was not found.");
                }
                
                // Perform soft delete
                submissionArtifact.DeletedAt = DateTime.UtcNow;
                
                // Update the entity
                await submissionArtifactRepo.UpdateAsync(submissionArtifact);
                
                // Save changes
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
                    $"Error deleting Submission Artifact: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetSubmissionArtifactDTO>> GetPaginatedSubmissionArtifactAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? submissionIdSearch)
        {
            try
            {
                // Get the repository
                IGenericRepository<SubmissionArtifact> submissionArtifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                
                // Build query with filters
                IQueryable<SubmissionArtifact> query = submissionArtifactRepo.Entities.Where(sa => sa.DeletedAt == null);
                
                // Apply id filter if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(sa => sa.ArtifactId == idSearch.Value);
                }
                
                // Apply submission id filter if provided
                if (submissionIdSearch.HasValue)
                {
                    query = query.Where(sa => sa.SubmissionId == submissionIdSearch.Value);
                }
                
                // Get paginated result
                PaginatedList<SubmissionArtifact> resultQuery = await submissionArtifactRepo.GetPagingAsync(query, pageNumber, pageSize);
                
                // Map entities to DTOs
                IReadOnlyCollection<GetSubmissionArtifactDTO> result = resultQuery.Items.Select(item => _mapper.Map<GetSubmissionArtifactDTO>(item)).ToList();

                // Create new paginated list with mapped items
                return new PaginatedList<GetSubmissionArtifactDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
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
                    $"Error retrieving Submission Artifacts: {ex.Message}");
            }
        }

        public async Task UpdateSubmissionArtifactAsync(Guid id, UpdateSubmissionArtifactDTO submissionArtifactDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get the repository
                IGenericRepository<SubmissionArtifact> submissionArtifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
                
                // Find the artifact by id
                SubmissionArtifact? submissionArtifact = await submissionArtifactRepo.GetByIdAsync(id);
                
                // Check if artifact exists
                if (submissionArtifact == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission Artifact with ID {id} was not found.");
                }
                
                // Update entity properties from DTO
                _mapper.Map(submissionArtifactDTO, submissionArtifact);
                
                // Update the entity
                await submissionArtifactRepo.UpdateAsync(submissionArtifact);
                
                // Save changes
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
                    $"Error updating Submission Artifact: {ex.Message}");
            }
        }
    }
}
