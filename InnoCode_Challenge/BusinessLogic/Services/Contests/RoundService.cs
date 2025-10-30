using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class RoundService : IRoundService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IMcqTestService _mcqTestService;
        private readonly IProblemService _problemService;

        public RoundService(IMapper mapper, IUOW unitOfWork, IMcqTestService mcqTestService, IProblemService problemService)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _mcqTestService = mcqTestService;
            _problemService = problemService;
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

                // Validate against contest dates and other rounds
                await ValidateRoundDatesAsync(roundDTO.ContestId, roundDTO.Start, roundDTO.End, null);


                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Map DTO to Entity
                Round round = _mapper.Map<Round>(roundDTO);

                // Insert new round
                await roundRepo.InsertAsync(round);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Handle problem type specific logic
                switch (roundDTO.ProblemType)
                {
                    case ProblemTypeEnum.McqTest:
                        await _mcqTestService.CreateMcqTestAsync(round.RoundId, new CreateMcqTestDTO
                        {
                            Name = roundDTO.McqTestConfig?.Name ?? "Default MCQ Test",
                            Config = roundDTO.McqTestConfig?.Config
                        });
                        break;

                    case ProblemTypeEnum.AutoEvaluation:
                        await _problemService.CreateProblemAsync(round.RoundId, new CreateProblemDTO
                        {
                            Type = ProblemTypeEnum.AutoEvaluation,
                            Description = roundDTO.ProblemConfig?.Description ?? "Default Auto Evaluation Problem",
                            Language = roundDTO.ProblemConfig?.Language ?? "python3",
                            PenaltyRate = roundDTO.ProblemConfig?.PenaltyRate ?? 0
                        });
                        break;

                    case ProblemTypeEnum.Manual:
                        await _problemService.CreateProblemAsync(round.RoundId, new CreateProblemDTO
                        {
                            Type = ProblemTypeEnum.Manual,
                            Description = roundDTO.ProblemConfig?.Description ?? "Default Manual Problem",
                            Language = roundDTO.ProblemConfig?.Language ?? "python3",
                            PenaltyRate = roundDTO.ProblemConfig?.PenaltyRate ?? 0
                        });
                        break;

                    default:
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid problem type.");
                }

                // Save all changes
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

                // Find round by id with related entities
                Round? round = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .Include(r => r.McqTests)
                    .FirstOrDefaultAsync(r => r.RoundId == id);

                // Check if round exists
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");
                }

                //// Delete related Problem if exists
                //if (round.Problem != null)
                //{
                //    IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                //    round.Problem.DeletedAt = DateTime.UtcNow;
                //    await problemRepo.UpdateAsync(round.Problem);
                //}

                // Delete related Problem if exists
                if (round.Problem != null)
                {
                    IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                    await problemRepo.DeleteAsync(round.Problem);
                }

                // Delete related McqTests if exists
                if (round.McqTests.Any())
                {
                    IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();
                    foreach (var mcqTest in round.McqTests)
                    {
                        await mcqTestRepo.DeleteAsync(mcqTest);
                    }
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

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Round: {ex.Message}");
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

                // Get all rounds with related entities
                IQueryable<Round> query = roundRepo.Entities
                    .Include(r => r.Contest)
                    .Include(r => r.Problem)
                    .Include(r => r.McqTests);

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

                    // Map problem information if exists
                    if (item.Problem != null && item.Problem.DeletedAt == null)
                    {
                        roundDTO.ProblemType = item.Problem.Type;
                        roundDTO.Problem = _mapper.Map<GetProblemDTO>(item.Problem);
                    }
                    // Map MCQ test information if exists
                    else if (item.McqTests.Any())
                    {
                        roundDTO.ProblemType = ProblemTypeEnum.McqTest.ToString();
                        roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(item.McqTests.First());
                    }

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
                if (ex is ErrorException)
                {
                    throw;
                }

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

                // Validate against contest dates and other rounds (excluding current round)
                await ValidateRoundDatesAsync(round.ContestId, roundDTO.Start, roundDTO.End, id);

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

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Round: {ex.Message}");
            }
        }

        private async Task ValidateRoundDatesAsync(Guid contestId, DateTime roundStart, DateTime roundEnd, Guid? excludeRoundId)
        {
            // Get Contest Repository
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

            // Fetch the contest
            Contest? contest = await contestRepo.GetByIdAsync(contestId);

            // Check if contest exists
            if (contest == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
            }

            // Validate round dates are within contest dates
            if (contest.Start.HasValue && roundStart < contest.Start.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round start date cannot be before contest start date ({contest.Start.Value:yyyy-MM-dd HH:mm:ss}).");
            }

            if (contest.End.HasValue && roundEnd > contest.End.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round end date cannot be after contest end date ({contest.End.Value:yyyy-MM-dd HH:mm:ss}).");
            }

            // Get Round Repository
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

            // Get all rounds for this contest (excluding the current round if updating)
            IQueryable<Round> existingRoundsQuery = roundRepo.Entities
                .Where(r => r.ContestId == contestId);

            if (excludeRoundId.HasValue)
            {
                existingRoundsQuery = existingRoundsQuery.Where(r => r.RoundId != excludeRoundId.Value);
            }

            List<Round> existingRounds = await existingRoundsQuery.ToListAsync();

            // Check for date conflicts with existing rounds
            foreach (Round existingRound in existingRounds)
            {
                // Check if dates overlap
                if (roundStart <= existingRound.End && roundEnd >= existingRound.Start)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        $"Round dates conflict with existing round '{existingRound.Name}' ({existingRound.Start:yyyy-MM-dd HH:mm:ss} - {existingRound.End:yyyy-MM-dd HH:mm:ss}).");
                }
            }
        }
    }
}
