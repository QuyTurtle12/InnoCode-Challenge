using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ContestDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class ContestService : IContestService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private const int MIN_YEAR = 10;

        // Constructor
        public ContestService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task DeleteContestAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate contest ID
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
                }

                // Get repository and fetch the contest by ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                Contest? existingContest = await contestRepo.GetByIdAsync(id);

                // Check if the contest exists
                if (existingContest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Check if the contest is already deleted
                if (existingContest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest already deleted.");
                }

                // Soft delete by setting the DeletedAt property
                existingContest.DeletedAt = DateTime.UtcNow;

                // Set status to Cancelled
                existingContest.Status = ContestStatusEnum.Cancelled.ToString();

                // Update the contest
                await contestRepo.UpdateAsync(existingContest);

                // Save changes to database
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
                    $"Error deleting Contests: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(int pageNumber, int pageSize, Guid? idSearch, string? nameSearch, int? yearSearch, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Validate year range
                if (yearSearch.HasValue)
                {
                    int currentYear = DateTime.UtcNow.Year;
                    if (yearSearch < MIN_YEAR)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, $"Year must be greater than 1900");
                    }
                }

                // Validate date range
                if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
                }

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Get all available contests
                IQueryable<Contest> query = contestRepo.Entities.Where(c => !c.DeletedAt.HasValue);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(c => c.ContestId == idSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(nameSearch))
                {
                    query = query.Where(c => c.Name.Contains(nameSearch));
                }

                if (yearSearch.HasValue)
                {
                    query = query.Where(c => c.Year == yearSearch.Value);
                }

                if (startDate.HasValue)
                {
                    query = query.Where(c => c.Start >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(c => c.End <= endDate.Value);
                }

                // Order by creation date (newest first)
                query = query.OrderByDescending(c => c.CreatedAt);

                // Change to paginated list to facilitate mapping process
                PaginatedList<Contest> resultQuery = await contestRepo.GetPagingAsync(query, pageNumber, pageSize);

                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Extract all contest IDs from the results
                List<Guid> contestIds = resultQuery.Items.Select(c => c.ContestId).ToList();

                // Load all configs for all contests in one query (performance optimization)
                List<Config> configs = await configRepo.Entities
                    .Where(c => contestIds.Any(id => c.Key.Contains(id.ToString())) && c.DeletedAt == null)
                    .ToListAsync();

                // Create a lookup dictionary for faster access
                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                // Map the result to DTO
                IReadOnlyCollection<GetContestDTO> result = resultQuery.Items.Select(item =>
                {
                    // Map base properties
                    GetContestDTO contestDTO = _mapper.Map<GetContestDTO>(item);

                    // Convert start/end to ISO 8601 format
                    if (contestDTO.Start.HasValue)
                        contestDTO.Start = DateTime.SpecifyKind(contestDTO.Start.Value, DateTimeKind.Local);
                    if (contestDTO.End.HasValue)
                        contestDTO.End = DateTime.SpecifyKind(contestDTO.End.Value, DateTimeKind.Local);

                    // Fetch team members max from config
                    string teamMemberMaxKey = ConfigKeys.ContestTeamMembersMax(item.ContestId);
                    Config? teamMemberMaxConfig = configLookup[teamMemberMaxKey].FirstOrDefault();
                    if (teamMemberMaxConfig != null && int.TryParse(teamMemberMaxConfig.Value, out int teamMemberMax))
                    {
                        contestDTO.TeamMembersMax = teamMemberMax;
                    }

                    // Fetch team limit max from config
                    string teamLimitMaxKey = ConfigKeys.ContestTeamLimitMax(item.ContestId);
                    Config? teamLimitMaxConfig = configLookup[teamLimitMaxKey].FirstOrDefault();
                    if (teamLimitMaxConfig != null && int.TryParse(teamLimitMaxConfig.Value, out int teamLimitMax))
                    {
                        contestDTO.TeamLimitMax = teamLimitMax;
                    }

                    // Fetch registration start from config
                    string regStartKey = ConfigKeys.ContestRegStart(item.ContestId);
                    Config? regStartConfig = configLookup[regStartKey].FirstOrDefault();
                    if (regStartConfig != null && DateTime.TryParse(regStartConfig.Value, out DateTime regStart))
                    {
                        contestDTO.RegistrationStart = regStart;
                    }

                    // Fetch registration end from config
                    string regEndKey = ConfigKeys.ContestRegEnd(item.ContestId);
                    Config? regEndConfig = configLookup[regEndKey].FirstOrDefault();
                    if (regEndConfig != null && DateTime.TryParse(regEndConfig.Value, out DateTime regEnd))
                    {
                        contestDTO.RegistrationEnd = regEnd;
                    }

                    // Fetch rewards text from config
                    string rewardsKey = ConfigKeys.ContestRewards (item.ContestId);
                    Config? rewardsConfig = configLookup[rewardsKey].FirstOrDefault();
                    if (rewardsConfig != null && !string.IsNullOrWhiteSpace(rewardsConfig.Value))
                    {
                        contestDTO.RewardsText = rewardsConfig.Value;
                    }

                    return contestDTO;
                }).ToList();

                // Create a new paginated list with the mapped DTOs
                PaginatedList<GetContestDTO> paginatedList = new PaginatedList<GetContestDTO>(result, resultQuery.TotalCount, resultQuery.PageNumber, resultQuery.PageSize);

                // Return the paginated list of DTOs
                return paginatedList;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Contests: {ex.Message}");
            }
        }

        public async Task<GetContestDTO> UpdateContestAsync(Guid id, UpdateContestDTO contestDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (contestDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest data cannot be null.");
                }

                // Validate contest ID
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
                }

                // Validate year
                if (contestDTO.Year < DateTime.UtcNow.Year)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest year cannot be in the past.");
                }

                // Validate date ranges
                if (contestDTO.Start.HasValue && contestDTO.End.HasValue && contestDTO.Start.Value >= contestDTO.End.Value)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest start date must be earlier than end date.");
                }

                // Validate registration dates
                if (contestDTO.RegistrationStart.HasValue && contestDTO.RegistrationEnd.HasValue && contestDTO.RegistrationStart.Value >= contestDTO.RegistrationEnd.Value)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration start date must be earlier than registration end date.");
                }

                // Validate registration start date vs contest dates
                if (contestDTO.RegistrationStart.HasValue && contestDTO.Start.HasValue && contestDTO.RegistrationStart.Value >= contestDTO.Start.Value)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration start date must be earlier than contest start date.");
                }

                // Validate registration end date vs contest dates
                if (contestDTO.RegistrationEnd.HasValue && contestDTO.Start.HasValue && contestDTO.RegistrationEnd.Value >= contestDTO.Start.Value)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration end date must be earlier than contest start date.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(contestDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest name is required.");
                }

                // Get repository and fetch the contest by ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get existing contest
                Contest? existingContest = await contestRepo.GetByIdAsync(id);

                // Check if the contest exists
                if (existingContest == null || existingContest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Trim name for consistent checking
                string nameTrim = contestDTO.Name.Trim();

                // Check if a contest with the same name and year already exists
                bool exists = await contestRepo.Entities
                    .AnyAsync(c => c.Year == contestDTO.Year && (c.Name == nameTrim && c.Name != existingContest.Name) && c.DeletedAt == null);

                // Check for duplicate contest name in the same year
                if (exists)
                {
                    string? suggestion = await SuggestAlternateNameAsync(nameTrim, contestDTO.Year, contestRepo);
                    CoreException ex = new CoreException(ResponseCodeConstants.DUPLICATE, "Contest name already exists for this year.", StatusCodes.Status409Conflict)
                    {
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["suggestion"] = suggestion
                        }
                    };
                    throw ex;
                }

                // Update properties from DTO
                _mapper.Map(contestDTO, existingContest);

                // Set contest-specific configurations
                int teamMembersMax = contestDTO.TeamMembersMax
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4);
                int? teamLimitMax = contestDTO.TeamLimitMax
                                     ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);

                // Insert or update config entries
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMax(existingContest.ContestId), teamMembersMax.ToString());

                // Set team limit max
                if (teamLimitMax.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamLimitMax(existingContest.ContestId), teamLimitMax.Value.ToString());

                // Set registration times
                if (contestDTO.RegistrationStart.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(existingContest.ContestId), contestDTO.RegistrationStart.Value.ToUniversalTime().ToString("o"));
                if (contestDTO.RegistrationEnd.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(existingContest.ContestId), contestDTO.RegistrationEnd.Value.ToUniversalTime().ToString("o"));

                // Set rewards text
                if (!string.IsNullOrWhiteSpace(contestDTO.RewardsText))
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRewards(existingContest.ContestId), contestDTO.RewardsText!.Trim());

                // Update the contest
                await contestRepo.UpdateAsync(existingContest);

                // Save changes to database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                // Return the updated contest DTO
                PaginatedList<GetContestDTO> result = await GetPaginatedContestAsync(1, 1, existingContest.ContestId, null, null, null, null);

                return result.Items.First();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                if (ex is CoreException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Contests: {ex.Message}");
            }
        }

        public async Task<ContestCreatedDTO> CreateContestWithPolicyAsync(CreateContestAdvancedDTO dto)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (dto == null)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Name is required.");

                int currentYear = DateTime.UtcNow.Year;
                if (dto.Year < currentYear)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, $"Year must be ≥ {currentYear}.");

                if (dto.RegistrationStart.HasValue && dto.RegistrationEnd.HasValue && dto.RegistrationStart.Value >= dto.RegistrationEnd.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration start must be before registration end.");

                if (dto.Start.HasValue && dto.End.HasValue && dto.Start.Value >= dto.End.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest start must be before contest end.");

                if (dto.RegistrationStart.HasValue && dto.Start.HasValue && dto.RegistrationStart.Value >= dto.Start.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration start must be before contest start.");

                if (dto.RegistrationEnd.HasValue && dto.Start.HasValue && dto.RegistrationEnd.Value >= dto.Start.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration end must be before contest start.");

                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Trim name for consistent checking
                string nameTrim = dto.Name.Trim();

                // Check if a contest with the same name and year already exists
                bool exists = await contestRepo.Entities
                    .AnyAsync(c => c.Year == dto.Year && c.Name == nameTrim && c.DeletedAt == null);

                // Check for duplicate contest name in the same year
                if (exists)
                {
                    string? suggestion = await SuggestAlternateNameAsync(nameTrim, dto.Year, contestRepo);
                    CoreException ex = new CoreException(ResponseCodeConstants.DUPLICATE, "Contest name already exists for this year.", StatusCodes.Status409Conflict)
                    {
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["suggestion"] = suggestion
                        }
                    };
                    throw ex;
                }

                // Map DTO to entity
                Contest entity = _mapper.Map<Contest>(dto);
                entity.ContestId = Guid.NewGuid();
                entity.Name = nameTrim;
                entity.Status = ContestStatusEnum.Draft.ToString();
                entity.CreatedAt = DateTime.UtcNow;

                // Insert the new contest
                await contestRepo.InsertAsync(entity);
                await _unitOfWork.SaveAsync();

                // Set contest-specific configurations
                int teamMembersMax = dto.TeamMembersMax
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4);
                int? teamLimitMax = dto.TeamLimitMax
                                     ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);

                // Insert or update config entries
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMax(entity.ContestId), teamMembersMax.ToString());
                
                // Set team limit max
                if (teamLimitMax.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamLimitMax(entity.ContestId), teamLimitMax.Value.ToString());

                // Set registration times
                if (dto.RegistrationStart.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(entity.ContestId), dto.RegistrationStart.Value.ToString("o"));
                if (dto.RegistrationEnd.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(entity.ContestId), dto.RegistrationEnd.Value.ToString("o"));

                // Set rewards text
                if (!string.IsNullOrWhiteSpace(dto.RewardsText))
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRewards(entity.ContestId), dto.RewardsText!.Trim());

                // Save all config changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                // Map to created DTO
                ContestCreatedDTO created = _mapper.Map<ContestCreatedDTO>(entity);
                created.TeamMembersMax = teamMembersMax;
                created.TeamLimitMax = teamLimitMax;
                created.RewardsText = dto.RewardsText;
                created.RegistrationStart = dto.RegistrationStart;
                created.RegistrationEnd = dto.RegistrationEnd;

                // Return the created contest DTO
                return created;
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                if (ex is CoreException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Contests: {ex.Message}");
            }
        }

        public async Task<PublishReadinessDTO> CheckPublishReadinessAsync(Guid contestId)
        {
            // Get repositories
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Fetch the contest with its rounds
            Contest? contest = await contestRepo.Entities
                .Include(c => c.Rounds)
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            // Validate contest existence
            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            // Initialize result DTO
            PublishReadinessDTO result = new PublishReadinessDTO { ContestId = contestId };

            // Check for rounds
            if (contest.Rounds == null || !contest.Rounds.Any())
                result.Missing.Add("No rounds created.");

            // Check for problems in each round
            List<Guid>? roundIds = contest.Rounds!
                .Where(r => !r.DeletedAt.HasValue)
                .Select(r => r.RoundId)
                .ToList();

            if (roundIds.Any())
            {
                // Count problems associated with the contest's rounds
                int problemCount = await problemRepo.Entities.CountAsync(p => roundIds.Contains(p.RoundId) && p.DeletedAt == null);
                if (problemCount < roundIds.Count)
                    result.Missing.Add("One or more rounds missing a problem.");
            }

            // Check registration window configuration
            string? regStart = await configRepo.Entities
                .Where(c => c.Key == $"contest:{contestId}:registration_start" && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? regEnd = await configRepo.Entities
                .Where(c => c.Key == $"contest:{contestId}:registration_end" && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            // Validate registration window presence
            if (string.IsNullOrEmpty(regStart) || string.IsNullOrEmpty(regEnd))
                result.Missing.Add("Registration window not configured.");

            // Check team members max configuration
            string? membersMaxContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMax(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            // Fallback to global default if contest-specific not set
            string? membersMaxDefault = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMax && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            // Validate team members max presence
            if (string.IsNullOrEmpty(membersMaxContest) && string.IsNullOrEmpty(membersMaxDefault))
                result.Missing.Add("Team members max not configured (contest or global).");

            // Final readiness determination
            result.IsReady = result.Missing.Count == 0;
            return result;
        }

        public async Task PublishIfReadyAsync(Guid contestId)
        {
            try
            {
                // Check publish readiness
                PublishReadinessDTO check = await CheckPublishReadinessAsync(contestId);

                // If not ready, throw exception with details
                if (!check.IsReady)
                {
                    CoreException ex = new CoreException("PUBLISH_BLOCKED", "Contest is not ready to publish.", StatusCodes.Status409Conflict)
                    {
                        AdditionalData = new Dictionary<string, object> { ["missing"] = check.Missing }
                    };
                    throw ex;
                }

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Fetch the contest
                Contest? contest = await contestRepo.GetByIdAsync(contestId);

                // Validate contest existence
                if (contest == null || contest.DeletedAt != null)
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

                // Update contest status to Published
                contest.Status = ContestStatusEnum.Published.ToString();
                await contestRepo.UpdateAsync(contest);
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                if (ex is CoreException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Contests: {ex.Message}");
            }
        }


        private static async Task UpsertConfigAsync(IGenericRepository<Config> repo, string key, string value)
        {
            // Check for existing config
            Config? existing = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key);

            // Insert or update accordingly
            if (existing == null)
            {
                await repo.InsertAsync(new Config
                {
                    Key = key,
                    Value = value,
                    Scope = "contest",
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                await repo.UpdateAsync(existing);
            }
        }

        private static async Task<string> SuggestAlternateNameAsync(string baseName, int year, IGenericRepository<Contest> repo)
        {
            // Suggest names with numeric suffixes until an available one is found
            int suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }
            while (await repo.Entities.AnyAsync(c => c.Year == year && c.Name == candidate && c.DeletedAt == null));

            return candidate;
        }
        private static async Task<int> GetGlobalIntOrDefaultAsync(IGenericRepository<Config> repo, string key, int @default)
        {
            // Fetch the config value
            string? value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                           .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out int n) ? n : @default;
        }

        private static async Task<int?> GetGlobalNullableIntAsync(IGenericRepository<Config> repo, string key)
        {
            // Fetch the config value
            string? value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                           .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out int n) ? n : null;
        }

        public async Task CancelledContest(Guid contestId)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Fetch the contest
                Contest? existingContest = await contestRepo
                    .Entities
                    .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Validate contest existence
                if (existingContest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Update contest status to Cancelled
                existingContest.Status = ContestStatusEnum.Cancelled.ToString();

                // Save to database
                await contestRepo.UpdateAsync(existingContest);
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
                    $"Error creating Contests: {ex.Message}");
            }
        }
    }
}
