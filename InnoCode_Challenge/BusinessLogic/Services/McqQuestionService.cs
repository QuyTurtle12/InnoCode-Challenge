using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.McqQuestionDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class McqQuestionService : IMcqQuestionService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public McqQuestionService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task CreateMcqQuestion(CreateMcqQuestionDTO mcqQuestionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Map DTO to entity
                McqQuestion mcqQuestion = _mapper.Map<McqQuestion>(mcqQuestionDTO);
                mcqQuestion.CreatedAt = DateTime.UtcNow;

                // Get repository and insert question
                IGenericRepository<McqQuestion> mcqQuestionRepo = _unitOfWork.GetRepository<McqQuestion>();
                await mcqQuestionRepo.InsertAsync(mcqQuestion);
                
                // Save changes
                await _unitOfWork.SaveAsync();
                
                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw;
            }
        }

        public async Task DeleteMcqQuestion(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get repository
                IGenericRepository<McqQuestion> mcqQuestionRepo = _unitOfWork.GetRepository<McqQuestion>();
                
                // Find the entity by ID
                McqQuestion? mcqQuestion = await mcqQuestionRepo.GetByIdAsync(id);
                
                // Check if entity exists
                if (mcqQuestion == null)
                {
                    throw new KeyNotFoundException($"McqQuestion with ID {id} not found");
                }
                
                // Apply soft delete
                mcqQuestion.DeletedAt = DateTime.UtcNow;
                
                // Update the entity
                await mcqQuestionRepo.UpdateAsync(mcqQuestion);
                
                // Save changes
                await _unitOfWork.SaveAsync();
                
                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw;
            }
        }

        public async Task<PaginatedList<GetMcqQuestionDTO>> GetPaginatedMcqQuestionAsync(int pageNumber, int pageSize, Guid? idSearch)
        {
            // Validate pageNumber and pageSize
            if (pageNumber < 1 || pageSize < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
            }

            // Get repository for McqQuestion entities
            IGenericRepository<McqQuestion> mcqQuestionRepo = _unitOfWork.GetRepository<McqQuestion>();

            // Create base query
            IQueryable<McqQuestion> query = mcqQuestionRepo
                .Entities
                .Where(q => q.DeletedAt == null)
                .Include(q => q.Bank)
                .Include(q => q.McqOptions);

            // Apply ID filter if provided
            if (idSearch.HasValue)
            {
                query = query.Where(q => q.QuestionId == idSearch.Value);
            }

            // Order by CreatedAt descending
            query = query.OrderByDescending(q => q.CreatedAt);

            PaginatedList<McqQuestion> resultQuery = await mcqQuestionRepo.GetPagging(query, pageNumber, pageSize);

            // Project entities to DTOs using AutoMapper
            IReadOnlyCollection<GetMcqQuestionDTO> result = resultQuery.Items.Select(item =>
            {
                GetMcqQuestionDTO mcqQuestionDTO = new()
                {
                    QuestionId = item.QuestionId,
                    BankId = item.BankId ?? Guid.Empty,
                    BankName = item.Bank?.Name ?? string.Empty,
                    Text = item.Text,
                    //McqOptions = item.McqOptions.Select(o => new McqOptionDTO
                    //{
                    //    OptionId = o.OptionId,
                    //    Text = o.Text,
                    //    IsCorrect = o.IsCorrect
                    //}).ToList(),
                    CreatedAt = item.CreatedAt
                };

                return mcqQuestionDTO;
            }).ToList();

            // Create and return paginated list
            return new PaginatedList<GetMcqQuestionDTO>(
                result,
                resultQuery.TotalCount,
                resultQuery.PageNumber,
                resultQuery.PageSize
            );
        }

        public async Task UpdateMcqQuestion(Guid id, UpdateMcqQuestionDTO mcqQuestionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();
                
                // Get repository
                IGenericRepository<McqQuestion> mcqQuestionRepo = _unitOfWork.GetRepository<McqQuestion>();
                
                // Find the entity by ID
                McqQuestion? mcqQuestion = await mcqQuestionRepo.GetByIdAsync(id);
                
                // Check if entity exists
                if (mcqQuestion == null)
                {
                    throw new KeyNotFoundException($"McqQuestion with ID {id} not found");
                }
                
                // Map DTO to entity
                _mapper.Map(mcqQuestionDTO, mcqQuestion);
                
                // Update the entity
                await mcqQuestionRepo.UpdateAsync(mcqQuestion);
                
                // Save changes
                await _unitOfWork.SaveAsync();
                
                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw;
            }
        }
    }
}
