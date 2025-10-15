using AutoMapper;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.McqAttemptDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Mcqs
{
    public class McqAttemptService : IMcqAttemptService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public McqAttemptService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task CreateMcqAttemptAsync(CreateMcqAttemptDTO mcqAttemptDTO)
        {
            try
            {
                // Begin a new transaction
                _unitOfWork.BeginTransaction();

                // Map the DTO to entity
                McqAttempt mcqAttempt = _mapper.Map<McqAttempt>(mcqAttemptDTO);
                
                // Get mcq attempt repository
                IGenericRepository<McqAttempt> repository = _unitOfWork.GetRepository<McqAttempt>();

                // Insert the new MCQ attempt
                await repository.InsertAsync(mcqAttempt);
                
                // Save changes
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
                    $"Error creating MCQ Attempt: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetMcqAttemptDTO>> GetPaginatedMcqAttemptAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? testIdSearch, Guid? roundIdSearch, Guid? studentIdSearch, string? testName, string? roundName, string? studentName)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Get repository for McqAttempt
                IGenericRepository<McqAttempt> mcqAttemptRepo = _unitOfWork.GetRepository<McqAttempt>();

                // Start with base query
                IQueryable<McqAttempt> query = mcqAttemptRepo
                    .Entities
                    .Include(a => a.Test)
                    .Include(a => a.Round)
                    .Include(a => a.Student)
                        .ThenInclude(s => s.User);
                
                // Apply filters based on provided search parameters
                if (idSearch.HasValue)
                {
                    query = query.Where(a => a.AttemptId == idSearch.Value);
                }
                
                if (testIdSearch.HasValue)
                {
                    query = query.Where(a => a.TestId == testIdSearch.Value);
                }
                
                if (roundIdSearch.HasValue)
                {
                    query = query.Where(a => a.RoundId == roundIdSearch.Value);
                }
                
                if (studentIdSearch.HasValue)
                {
                    query = query.Where(a => a.StudentId == studentIdSearch.Value);
                }
                
                if (!string.IsNullOrWhiteSpace(testName))
                {
                    query = query.Where(a => a.Test.Name!.Contains(testName));
                }
                
                if (!string.IsNullOrWhiteSpace(roundName))
                {
                    query = query.Where(a => a.Round.Name.Contains(roundName));
                }
                
                if (!string.IsNullOrWhiteSpace(studentName))
                {
                    query = query.Where(a => a.Student.User.Fullname.Contains(studentName));
                }

                // Order by Start date descending
                query = query.OrderByDescending(a => a.Start);

                // Get paginated list of entities to facilitate mapping process to DTOs
                PaginatedList<McqAttempt> resultQuery = await mcqAttemptRepo.GetPagingAsync(query, pageNumber, pageSize);

                IReadOnlyCollection<GetMcqAttemptDTO> result = resultQuery.Items.Select(item =>
                {
                    GetMcqAttemptDTO mcqAttemptDTO = _mapper.Map<GetMcqAttemptDTO>(item);

                    mcqAttemptDTO.TestName = item.Test.Name ?? string.Empty;
                    mcqAttemptDTO.RoundName = item.Round.Name;
                    mcqAttemptDTO.StudentName = item.Student.User.Fullname;

                    return mcqAttemptDTO;
                }).ToList();

                // Create new paginated list with mapped DTOs
                return new PaginatedList<GetMcqAttemptDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving MCQ Attempts: {ex.Message}");
            }
        }
    }
}
