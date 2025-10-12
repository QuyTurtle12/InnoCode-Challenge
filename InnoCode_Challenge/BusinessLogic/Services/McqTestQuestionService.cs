using System;
using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.McqTestQuestionDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class McqTestQuestionService : IMcqTestQuestionService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        // Constructor
        public McqTestQuestionService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task CreateTestQuestionAsync(CreateMcqTestQuestionDTO createTestQuestionDTO)
        {
            try
            {
                // Begin a new transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to entity
                McqTestQuestion testQuestion = _mapper.Map<McqTestQuestion>(createTestQuestionDTO);

                // Get Mcq Test Question repository
                IGenericRepository<McqTestQuestion> McqTestRepo = _unitOfWork.GetRepository<McqTestQuestion>();

                // Insert new test question
                await McqTestRepo.InsertAsync(testQuestion);

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
                     $"Error creating Rounds: {ex.Message}"
                );
            }
        }

        public async Task DeleteTestQuestionAsync(Guid id)
        {
            try
            {
                // Begin a new transaction
                _unitOfWork.BeginTransaction();

                // Get Mcq Test Question repository
                IGenericRepository<McqTestQuestion> McqTestRepo = _unitOfWork.GetRepository<McqTestQuestion>();

                // Find the question by id
                var testQuestion = await McqTestRepo.GetByIdAsync(id);

                // If the question does not exist, throw a 404 error
                if (testQuestion == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Test question not found");
                }

                // Delete the test question
                await McqTestRepo.DeleteAsync(testQuestion);

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
                    $"Error deleting test question: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetMcqTestQuestionDTO>> GetPaginatedTestQuestionAsync(int pageNumber, int pageSize, Guid? testIdSearch, Guid? questionIdSearch)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number or page size must be greater than or equal to 1.");
                }

                // Get Mcq Test Question repository
                IGenericRepository<McqTestQuestion> McqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();

                // Create base query
                IQueryable<McqTestQuestion> query = McqTestQuestionRepo
                    .Entities
                    .Include(mtq => mtq.Test)
                    .Include(mtq => mtq.Question);

                // Apply filter by ID if provided
                if (testIdSearch.HasValue)
                {
                    query = query.Where(q => q.TestId == testIdSearch.Value);
                }

                if (questionIdSearch.HasValue)
                {
                    query = query.Where(q => q.QuestionId == questionIdSearch.Value);
                }

                // Get paginated data to facilitate mapping process to DTOs
                PaginatedList<McqTestQuestion> resultQuery = await McqTestQuestionRepo.GetPagging(query, pageNumber, pageSize);

                // Map entities to DTOs
                IReadOnlyCollection<GetMcqTestQuestionDTO> result = resultQuery.Items.Select(item => {
                    GetMcqTestQuestionDTO mcqTestQuestionDTO = _mapper.Map<GetMcqTestQuestionDTO>(item);

                    mcqTestQuestionDTO.TestName = item.Test?.Name ?? string.Empty;
                    mcqTestQuestionDTO.QuestionText = item.Question?.Text ?? string.Empty;
                    return mcqTestQuestionDTO;
                }).ToList();

                // Create new paginated list with mapped DTOs
                return new PaginatedList<GetMcqTestQuestionDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving paginated test questions: {ex.Message}");
            }
        }

        public async Task UpdateTestQuestionAsync(Guid id, UpdateMcqTestQuestionDTO updateTestQuestionDTO)
        {
            try
            {
                // Begin a new transaction
                _unitOfWork.BeginTransaction();

                // Get Mcq Test Question repository
                IGenericRepository<McqTestQuestion> McqTestRepo = _unitOfWork.GetRepository<McqTestQuestion>();

                // Find the question by id
                McqTestQuestion? testQuestion = await McqTestRepo.GetByIdAsync(id);

                // If the question does not exist, throw a 404 error
                if (testQuestion == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Test question not found");
                }

                // Update the entity with values from DTO
                _mapper.Map(updateTestQuestionDTO, testQuestion);

                // Update the test question
                await McqTestRepo.UpdateAsync(testQuestion);

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
                    $"Error updating test question: {ex.Message}");
            }
        }
    }
}
