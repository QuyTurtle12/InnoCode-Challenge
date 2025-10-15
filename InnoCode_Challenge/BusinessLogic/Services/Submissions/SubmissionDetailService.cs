using AutoMapper;
using BusinessLogic.IServices.Submissions;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Repository.DTOs.SubmissionDetailDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Submissions
{
    public class SubmissionDetailService : ISubmissionDetailService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        // Constructor
        public SubmissionDetailService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task CreateSubmissionDetailAsync(CreateSubmissionDetailDTO submissionDetailDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to entity
                SubmissionDetail submissionDetail = _mapper.Map<SubmissionDetail>(submissionDetailDTO);

                // Get the submission detail repository
                IGenericRepository<SubmissionDetail> submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                // Set creation timestamp
                submissionDetail.CreatedAt = DateTime.UtcNow;

                // Insert the new submission detail
                await submissionDetailRepo.InsertAsync(submissionDetail);

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
                    $"Error creating Submission Detail: {ex.Message}");
            }
        }

        public async Task DeleteSubmissionDetailAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get the submission detail repository
                IGenericRepository<SubmissionDetail> submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                // Find the submission detail by id
                SubmissionDetail? submissionDetail = await submissionDetailRepo.GetByIdAsync(id);

                // If not found, throw an error
                if (submissionDetail == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Submission Detail not found");
                }

                // Apply soft delete
                submissionDetail.DeletedAt = DateTime.UtcNow;

                // Update the submission detail
                await submissionDetailRepo.UpdateAsync(submissionDetail);

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
                    $"Error deleting Submission Detail: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetSubmissionDetailDTO>> GetPaginatedSubmissionDetailAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? submissionIdSearch, Guid? TestcaseId)
        {
            try
            {
                // Get the submission detail repository
                IGenericRepository<SubmissionDetail> submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();
                
                // Start with the basic query - filter out deleted items
                var query = submissionDetailRepo.Entities.Where(sd => sd.DeletedAt == null);
                
                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(sd => sd.DetailsId == idSearch.Value);
                }
                
                if (submissionIdSearch.HasValue)
                {
                    query = query.Where(sd => sd.SubmissionId == submissionIdSearch.Value);
                }
                
                if (TestcaseId.HasValue)
                {
                    query = query.Where(sd => sd.TestcaseId == TestcaseId.Value);
                }
                
                // Apply pagination and get the result set
                PaginatedList<SubmissionDetail> resultQuery = await submissionDetailRepo.GetPagingAsync(query, pageNumber, pageSize);
                
                // Map to DTOs
                IReadOnlyCollection<GetSubmissionDetailDTO> result = _mapper.Map<IReadOnlyCollection<GetSubmissionDetailDTO>>(resultQuery.Items);
                
                // Create and return the paginated DTO list
                return new PaginatedList<GetSubmissionDetailDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize
                );
            }
            catch (Exception ex)
            {
                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated Submission Details: {ex.Message}"
                );
            }
        }

        public async Task UpdateSubmissionDetailAsync(Guid id, UpdateSubmissionDetailDTO submissionDetailDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get the submission detail repository
                IGenericRepository<SubmissionDetail> submissionDetailRepo = _unitOfWork.GetRepository<SubmissionDetail>();

                // Find the submission detail by id
                SubmissionDetail? submissionDetail = await submissionDetailRepo.GetByIdAsync(id);

                // If not found, throw an error
                if (submissionDetail == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Submission Detail not found");
                }

                // Update properties from the DTO
                _mapper.Map(submissionDetailDTO, submissionDetail);

                // Update the submission detail
                await submissionDetailRepo.UpdateAsync(submissionDetail);

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
                    $"Error updating Submission Detail: {ex.Message}");
            }
        }
    }
}
