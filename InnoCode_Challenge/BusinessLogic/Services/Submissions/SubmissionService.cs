using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.SubmissionDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Submissions
{
    public class SubmissionService : ISubmissionService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        // Constructor
        public SubmissionService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task CreateSubmissionAsync(CreateSubmissionDTO submissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Map DTO to entity
                Submission submission = _mapper.Map<Submission>(submissionDTO);
                
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

                // Set creation timestamp
                submission.CreatedAt = DateTime.UtcNow;

                // Insert the new submission
                await submissionRepo.InsertAsync(submission);
                
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
                    $"Error creating Submission: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetSubmissionDTO>> GetPaginatedSubmissionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? problemIdSearch, Guid? SubmittedByStudentId, string? teamName, string? studentName)
        {
            try
            {
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                
                // Start with base query
                IQueryable<Submission> query = submissionRepo
                    .Entities
                    .Include(s => s.Team)
                    .Include(s => s.SubmittedByStudent)
                        .ThenInclude(st => st!.User);
                
                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(s => s.SubmissionId == idSearch.Value);
                }
                
                if (problemIdSearch.HasValue)
                {
                    query = query.Where(s => s.ProblemId == problemIdSearch.Value);
                }

                if (SubmittedByStudentId.HasValue)
                {
                    query = query.Where(s => s.SubmittedByStudentId == SubmittedByStudentId.Value);
                }

                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    query = query.Where(s => s.SubmittedByStudent!.User.Fullname.Contains(teamName));
                }

                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    query = query.Where(s => s.Team.Name.Contains(teamName));
                }
                
                // Get paginated data
                PaginatedList<Submission> resultQuery = await submissionRepo.GetPagingAsync(query, pageNumber, pageSize);
                
                // Map to DTOs
                IReadOnlyCollection<GetSubmissionDTO> result = resultQuery.Items.Select(item => _mapper.Map<GetSubmissionDTO>(item)).ToList();
                
                // Create new paginated list with mapped DTOs
                return new PaginatedList<GetSubmissionDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated Submissions: {ex.Message}");
            }
        }

        public async Task UpdateSubmissionAsync(Guid id, UpdateSubmissionDTO submissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get the submission repository
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                
                // Find the existing submission
                Submission? existingSubmission = await submissionRepo.GetByIdAsync(id);
                
                if (existingSubmission == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Submission with ID {id} not found.");
                }
                
                // Map DTO to entity
                _mapper.Map(submissionDTO, existingSubmission);
                
                // Update the submission
                await submissionRepo.UpdateAsync(existingSubmission);
                
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
                    $"Error updating Submission: {ex.Message}");
            }
        }
    }
}
