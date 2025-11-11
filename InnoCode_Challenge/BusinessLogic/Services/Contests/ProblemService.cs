using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
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

        public async Task<RubricTemplateDTO> GetRubricTemplateAsync(Guid roundId)
        {
            try
            {
                // Get repositories
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();

                // Get problem
                Problem? problem = await problemRepo.Entities
                    .Where(p => p.RoundId == roundId && !p.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                // Validate problem existence
                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Problem not found");
                }

                // Verify this is a manual problem type
                if (problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Rubric template is only available for manual problem types");
                }

                // Get all rubric criteria for this problem
                List<TestCase> rubricCriteria = await rubricRepo.Entities
                    .Where(tc => tc.ProblemId == problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString()
                        && !tc.DeleteAt.HasValue)
                    .OrderBy(tc => tc.TestCaseId)
                    .ToListAsync();

                // Validate test cases existence
                if (!rubricCriteria.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No rubric criteria found for problem {problem.ProblemId}");
                }

                // Map to DTO
                RubricTemplateDTO template = new RubricTemplateDTO
                {
                    ProblemId = problem.ProblemId,
                    ProblemDescription = problem.Description,
                    TotalMaxScore = rubricCriteria.Sum(r => r.Weight),
                    Criteria = rubricCriteria.Select((r, index) => new RubricCriterionDTO
                    {
                        RubricId = r.TestCaseId,
                        Description = r.Description ?? r.Input ?? "Criterion",
                        MaxScore = r.Weight,
                        Order = index + 1
                    }).ToList()
                };

                return template;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving rubric template: {ex.Message}");
            }
        }

        public async Task<RubricTemplateDTO> CreateRubricCriterionAsync(Guid roundId, CreateRubricDTO createRubricDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get repositories
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Validate round exists
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Round with ID {roundId} not found");
                }

                // Get the problem for this round
                Problem? problem = await problemRepo.Entities
                    .Where(p => p.RoundId == roundId && !p.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                // Check if problem exists
                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Problem not found for round {roundId}");
                }

                // Verify this is a manual problem type
                if (problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Rubric can only be created for manual problem types");
                }

                List<TestCase> createdCriteria = new List<TestCase>();

                // Create rubric criteria
                foreach (CreateRubricCriterionDTO criterion in createRubricDTO.Criteria)
                {
                    TestCase rubricCriterion = new TestCase
                    {
                        TestCaseId = Guid.NewGuid(),
                        ProblemId = problem.ProblemId,
                        Description = criterion.Description,
                        Type = TestCaseTypeEnum.Manual.ToString(),
                        Weight = criterion.MaxScore,
                        Input = null,
                        ExpectedOutput = null,
                        TimeLimitMs = null,
                        MemoryKb = null
                    };

                    await testCaseRepo.InsertAsync(rubricCriterion);
                    createdCriteria.Add(rubricCriterion);
                }

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                // Return created rubric template
                return new RubricTemplateDTO
                {
                    ProblemId = problem.ProblemId,
                    ProblemDescription = problem.Description,
                    TotalMaxScore = createdCriteria.Sum(c => c.Weight),
                    Criteria = createdCriteria.Select((c, index) => new RubricCriterionDTO
                    {
                        RubricId = c.TestCaseId,
                        Description = c.Description ?? string.Empty,
                        MaxScore = c.Weight,
                        Order = index + 1
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating rubric: {ex.Message}");
            }
        }

        public async Task<RubricTemplateDTO> UpdateRubricCriterionAsync(Guid roundId, UpdateRubricDTO updateRubricDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get repositories
                IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();

                // Validate round exists
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Round with ID {roundId} not found");
                }

                // Get the problem for this round
                Problem? problem = await problemRepo.Entities
                    .Where(p => p.RoundId == roundId && !p.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                if (problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Problem not found for round {roundId}");
                }

                // Verify this is a manual problem type
                if (problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Rubric can only be updated for manual problem types");
                }

                // Validate that all criteria have RubricId
                if (updateRubricDTO.Criteria.Any(c => !c.RubricId.HasValue))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "All criteria must have a RubricId for update operation. Use CreateRubricAsync to add new criteria.");
                }

                // Get the rubric IDs to update
                List<Guid> rubricIdsToUpdate = updateRubricDTO.Criteria
                    .Select(c => c.RubricId!.Value)
                    .ToList();

                // Check for duplicates in the request
                if (rubricIdsToUpdate.Count != rubricIdsToUpdate.Distinct().Count())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Duplicate rubric IDs found in the request.");
                }

                // Get existing rubric criteria
                List<TestCase> existingCriteria = await rubricRepo.Entities
                    .Where(tc => rubricIdsToUpdate.Contains(tc.TestCaseId)
                        && tc.ProblemId == problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString()
                        && !tc.DeleteAt.HasValue)
                    .ToListAsync();

                // Validate all rubric IDs exist
                if (existingCriteria.Count != rubricIdsToUpdate.Count)
                {
                    List<Guid> foundIds = existingCriteria.Select(c => c.TestCaseId).ToList();
                    List<Guid> notFoundIds = rubricIdsToUpdate.Except(foundIds).ToList();

                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"The following rubric IDs were not found or do not belong to this round: {string.Join(", ", notFoundIds)}");
                }

                // Create dictionary for efficient lookup
                Dictionary<Guid, TestCase> existingDict = existingCriteria.ToDictionary(c => c.TestCaseId);

                // Update existing criteria
                foreach (UpdateRubricCriterionDTO criterionDTO in updateRubricDTO.Criteria)
                {
                    TestCase existingCriterion = existingDict[criterionDTO.RubricId!.Value];

                    // Update properties
                    existingCriterion.Description = criterionDTO.Description;
                    existingCriterion.Weight = criterionDTO.MaxScore;

                    await rubricRepo.UpdateAsync(existingCriterion);
                }

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                // Get all rubric criteria for return
                List<TestCase> allCriteria = await rubricRepo.Entities
                    .Where(tc => tc.ProblemId == problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString()
                        && !tc.DeleteAt.HasValue)
                    .OrderBy(tc => tc.TestCaseId)
                    .ToListAsync();

                // Return updated rubric template
                return new RubricTemplateDTO
                {
                    ProblemId = problem.ProblemId,
                    ProblemDescription = problem.Description,
                    TotalMaxScore = allCriteria.Sum(c => c.Weight),
                    Criteria = allCriteria
                        .Select((c, index) => new RubricCriterionDTO
                        {
                            RubricId = c.TestCaseId,
                            Description = c.Description ?? string.Empty,
                            MaxScore = c.Weight,
                            Order = index + 1
                        })
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating rubric: {ex.Message}");
            }
        }

        public async Task DeleteRubricCriterionAsync(Guid rubricId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get rubric repository
                IGenericRepository<TestCase> rubricRepo = _unitOfWork.GetRepository<TestCase>();

                // Find the rubric criterion
                TestCase? rubricCriterion = await rubricRepo.Entities
                    .Include(tc => tc.Problem)
                    .FirstOrDefaultAsync(tc => tc.TestCaseId == rubricId
                        && tc.Type == TestCaseTypeEnum.Manual.ToString()
                        && !tc.DeleteAt.HasValue);

                if (rubricCriterion == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Rubric criterion not found or already deleted.");
                }

                // Verify the problem exists and is not deleted
                if (rubricCriterion.Problem.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Cannot delete rubric criterion for a deleted problem.");
                }

                // Verify this is a manual problem type
                if (rubricCriterion.Problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Can only delete rubric criteria for manual problem types.");
                }

                // Soft delete the rubric criterion
                rubricCriterion.DeleteAt = DateTime.UtcNow;
                await rubricRepo.UpdateAsync(rubricCriterion);

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting rubric criterion: {ex.Message}");
            }
        }
    }
}
