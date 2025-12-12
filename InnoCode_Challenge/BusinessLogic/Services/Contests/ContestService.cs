using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class ContestService : IContestService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICloudinaryService _cloudinaryService;

        private const int MIN_YEAR = 10;
        private const string CONTEST_IMAGE_FOLDER = "contest_images";

        // Constructor
        public ContestService(IMapper mapper, IUOW uow, IHttpContextAccessor httpContextAccessor, ICloudinaryService cloudinaryService)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
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

                // Soft delete
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

        public async Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(
            int pageNumber,
            int pageSize,
            Guid? idSearch,
            Guid? creatorIdSearch,
            Guid? roundIdSearch,
            string? nameSearch,
            int? yearSearch,
            DateTime? startDate,
            DateTime? endDate,
            bool isMyParticipatedContest,
            bool isMyContest)
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
                IQueryable<Contest> query = contestRepo
                    .Entities
                    .Where(c => !c.DeletedAt.HasValue)
                    .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                        .ThenInclude(r => r.Problem)
                    .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                        .ThenInclude(r => r.McqTest);

                // Get contests where the current logged-in student is a participant
                if (isMyParticipatedContest)
                {
                    // Get current user ID from HttpContext
                    string? userId = _httpContextAccessor.HttpContext?.User?
                        .FindFirstValue(ClaimTypes.NameIdentifier);

                    // If user ID is available, get the corresponding student ID
                    if (!string.IsNullOrEmpty(userId))
                    {
                        // Get student repository
                        IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();

                        // Find the student ID associated with the user ID
                        Guid? studentId = await studentRepo.Entities
                            .Where(s => s.UserId.ToString() == userId && s.DeletedAt == null)
                            .Select(s => s.StudentId)
                            .FirstOrDefaultAsync();

                        // If student ID is found, filter contests accordingly
                        if (studentId.HasValue)
                        {
                            // Include Teams and TeamMembers for filtering
                            query = query.Include(c => c.Teams)
                                         .ThenInclude(t => t.TeamMembers);

                            // Filter contests where student is in a team
                            query = query.Where(c => c.Teams.Any(t =>
                                t.TeamMembers.Any(tm => tm.StudentId == studentId.Value)
                                && t.DeletedAt == null));
                        }
                    }
                }

                // Get contests created by the current logged-in organizer
                if (isMyContest)
                {
                    // Get current user ID from HttpContext
                    string? userId = _httpContextAccessor.HttpContext?.User?
                        .FindFirstValue(ClaimTypes.NameIdentifier);

                    // If user ID is available, filter contests created by this user
                    if (!string.IsNullOrEmpty(userId))
                    {
                        query = query.Where(c => c.CreatedBy == userId);
                    }
                }

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(c => c.ContestId == idSearch.Value);
                }

                if (creatorIdSearch.HasValue)
                {
                    query = query.Where(c => Guid.Parse(c.CreatedBy!) == creatorIdSearch.Value);
                }

                if (roundIdSearch.HasValue)
                {
                    query = query.Where(c => c.Rounds.Any(r => r.RoundId == roundIdSearch));
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

                // Order contest by creation date (newest first)
                query = query.OrderByDescending(c => c.CreatedAt);

                // Change to paginated list to facilitate mapping process
                PaginatedList<Contest> resultQuery = await contestRepo.GetPagingAsync(query, pageNumber, pageSize);

                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Extract all contest IDs from the results
                List<Guid> contestIds = resultQuery.Items.Select(c => c.ContestId).ToList();

                // Load all configs for all contests in one query
                List<Config> configs = await configRepo.Entities
                    .Where(c => contestIds.Any(id => c.Key.Contains(id.ToString())) && c.DeletedAt == null)
                    .ToListAsync();

                // Create a lookup dictionary for faster access
                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                // Extract distinct organizer IDs from the results
                List<string?> organizerIds = resultQuery.Items
                    .Select(c => c.CreatedBy)
                    .Distinct()
                    .ToList()!;

                // Get user repository
                IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();

                // Parse organizer IDs to Guids
                List<Guid> organizerGuids = organizerIds
                    .Select(id => Guid.TryParse(id, out Guid guid) ? guid : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToList();

                // Create a dictionary to map organizer IDs to names from User table
                Dictionary<Guid, string> organizerNames = await userRepo
                    .Entities
                    .Where(u => organizerGuids.Contains(u.UserId) && !u.DeletedAt.HasValue)
                    .ToDictionaryAsync(u => u.UserId, u => u.Fullname);

                // Extract distinct round IDs
                List<Guid> roundIds = resultQuery.Items
                    .SelectMany(c => c.Rounds)
                    .Select(r => r.RoundId)
                    .Distinct()
                    .ToList();

                // Load time limit configs for all rounds in one query
                List<Config> timeLimitConfigs = await configRepo.Entities
                    .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                                && c.Key.Contains("time_limit_seconds")
                                && c.DeletedAt == null)
                    .ToListAsync();

                // Create a lookup for fast access
                Dictionary<string, Config> timeLimitDict = timeLimitConfigs.ToDictionary(c => c.Key);

                // Map the result to DTO
                IReadOnlyCollection<GetContestDTO> result = resultQuery.Items.Select(item =>
                {
                    // Map base properties
                    GetContestDTO contestDTO = _mapper.Map<GetContestDTO>(item);

                    // Assign non-deleted rounds
                    contestDTO.rounds = item.Rounds
                    .Where(r => !r.DeletedAt.HasValue)
                    .Select(r =>
                    {
                        // Map base round properties
                        GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(r);

                        // Assign additional properties
                        roundDTO.RoundName = r.Name;
                        roundDTO.ContestName = item.Name;
                        roundDTO.Start = r.Start;
                        roundDTO.End = r.End;

                        // Fetch time limit from config
                        string timeLimitKey = ConfigKeys.RoundTimeLimitSeconds(r.RoundId);
                        if (timeLimitDict.TryGetValue(timeLimitKey, out Config? timeLimitConfig)
                            && int.TryParse(timeLimitConfig.Value, out int timeLimit))
                        {
                            roundDTO.TimeLimitSeconds = timeLimit;
                        }

                        // Map problem information if exists
                        if (r.Problem != null)
                        {
                            roundDTO.ProblemType = r.Problem.Type;
                            roundDTO.Problem = _mapper.Map<GetProblemDTO>(r.Problem);
                        }
                        // Map MCQ test information if exists
                        else if (r.McqTest != null)
                        {
                            roundDTO.ProblemType = ProblemTypeEnum.McqTest.ToString();
                            roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(r.McqTest);
                        }

                        return roundDTO;
                    })
                    .OrderBy(r => r.Start).ToList();

                    // Map creator ID
                    contestDTO.CreatedById = Guid.Parse(item.CreatedBy!);

                    // Map creator name
                    contestDTO.CreatedByName = item.CreatedBy != null && organizerNames.ContainsKey(contestDTO.CreatedById)
                        ? organizerNames[contestDTO.CreatedById]
                        : "Unknown Organizer";

                    // Map time properties
                    contestDTO.Start = item.Start;
                    contestDTO.End = item.End;
                    contestDTO.CreatedAt = item.CreatedAt;

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
                    string rewardsKey = ConfigKeys.ContestRewards(item.ContestId);
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

        public async Task<GetContestDTO> GetContestByIdAsync(Guid id)
        {
            try
            {
                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Get contest
                Contest? contest = await contestRepo
                    .Entities
                    .Where(c => c.ContestId == id && !c.DeletedAt.HasValue)
                    .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                        .ThenInclude(r => r.Problem)
                    .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                        .ThenInclude(r => r.McqTest)
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync();

                // Create a queryable for mapping
                if (contest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Contest not found.");
                }

                // Get related configs
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Load all configs for a contest in one query
                List<Config> configs = await configRepo.Entities
                    .Where(c => c.Key.Contains(id.ToString()) && c.DeletedAt == null)
                    .ToListAsync();

                // Create a lookup dictionary for faster access
                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                // Extract distinct organizer IDs from the results
                string? organizerId = contest.CreatedBy;

                // Get user repository
                IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();

                // Parse organizer ID to Guid
                Guid organizerGuid = Guid.TryParse(organizerId, out Guid guid) ? guid : Guid.Empty;

                // Get organizer name
                string organizerName = await userRepo
                    .Entities
                    .Where(u => organizerGuid == u.UserId && !u.DeletedAt.HasValue)
                    .Select(u => u.Fullname)
                    .FirstOrDefaultAsync() ?? "Unknown Organizer";

                // Extract distinct round ID
                List<Guid> roundIds = contest.Rounds
                    .Select(r => r.RoundId)
                    .Distinct()
                    .ToList();

                // Load time limit configs for all rounds in one query
                List<Config> timeLimitConfigs = await configRepo.Entities
                    .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                                && c.Key.Contains("time_limit_seconds")
                                && c.DeletedAt == null)
                    .ToListAsync();

                // Create a lookup for fast access
                Dictionary<string, Config> timeLimitDict = timeLimitConfigs.ToDictionary(c => c.Key);

                Guid createdById = Guid.Parse(contest.CreatedBy!);

                // Map the result to DTO
                GetContestDTO contestDTO = _mapper.Map<GetContestDTO>(contest);

                // Assign non-deleted rounds
                contestDTO.rounds = contest.Rounds
                .Where(r => !r.DeletedAt.HasValue)
                .Select(r =>
                {
                    // Map base round properties
                    GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(r);

                    // Assign additional properties
                    roundDTO.RoundName = r.Name;
                    roundDTO.ContestName = contest.Name;
                    roundDTO.Start = r.Start;
                    roundDTO.End = r.End;

                    // Fetch time limit from config
                    string timeLimitKey = ConfigKeys.RoundTimeLimitSeconds(r.RoundId);
                    if (timeLimitDict.TryGetValue(timeLimitKey, out Config? timeLimitConfig)
                        && int.TryParse(timeLimitConfig.Value, out int timeLimit))
                    {
                        roundDTO.TimeLimitSeconds = timeLimit;
                    }

                    // Map problem information if exists
                    if (r.Problem != null)
                    {
                        roundDTO.ProblemType = r.Problem.Type;
                        roundDTO.Problem = _mapper.Map<GetProblemDTO>(r.Problem);
                    }
                    // Map MCQ test information if exists
                    else if (r.McqTest != null)
                    {
                        roundDTO.ProblemType = ProblemTypeEnum.McqTest.ToString();
                        roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(r.McqTest);
                    }

                    return roundDTO;
                })
                .OrderBy(r => r.Start).ToList();

                // Map remain values
                contestDTO.CreatedById = createdById;
                contestDTO.CreatedByName = organizerName;
                contestDTO.Start = contest.Start;
                contestDTO.End = contest.End;
                contestDTO.CreatedAt = contest.CreatedAt;

                // Fetch team members max from config
                string teamMemberMaxKey = ConfigKeys.ContestTeamMembersMax(contest.ContestId);
                Config? teamMemberMaxConfig = configLookup[teamMemberMaxKey].FirstOrDefault();
                if (teamMemberMaxConfig != null && int.TryParse(teamMemberMaxConfig.Value, out int teamMemberMax))
                {
                    contestDTO.TeamMembersMax = teamMemberMax;
                }

                // Fetch team limit max from config
                string teamLimitMaxKey = ConfigKeys.ContestTeamLimitMax(contest.ContestId);
                Config? teamLimitMaxConfig = configLookup[teamLimitMaxKey].FirstOrDefault();
                if (teamLimitMaxConfig != null && int.TryParse(teamLimitMaxConfig.Value, out int teamLimitMax))
                {
                    contestDTO.TeamLimitMax = teamLimitMax;
                }

                // Fetch registration start from config
                string regStartKey = ConfigKeys.ContestRegStart(contest.ContestId);
                Config? regStartConfig = configLookup[regStartKey].FirstOrDefault();
                if (regStartConfig != null && DateTime.TryParse(regStartConfig.Value, out DateTime regStart))
                {
                    contestDTO.RegistrationStart = regStart;
                }

                // Fetch registration end from config
                string regEndKey = ConfigKeys.ContestRegEnd(contest.ContestId);
                Config? regEndConfig = configLookup[regEndKey].FirstOrDefault();
                if (regEndConfig != null && DateTime.TryParse(regEndConfig.Value, out DateTime regEnd))
                {
                    contestDTO.RegistrationEnd = regEnd;
                }

                // Fetch rewards text from config
                string rewardsKey = ConfigKeys.ContestRewards(contest.ContestId);
                Config? rewardsConfig = configLookup[rewardsKey].FirstOrDefault();
                if (rewardsConfig != null && !string.IsNullOrWhiteSpace(rewardsConfig.Value))
                {
                    contestDTO.RewardsText = rewardsConfig.Value;
                }

                // Return contest data
                return contestDTO;
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

                if (contestDTO.Start.HasValue)
                    existingContest.Start = contestDTO.Start.Value;
                if (contestDTO.End.HasValue)
                    existingContest.End = contestDTO.End.Value;

                // Handle image upload if a new image file is provided
                if (contestDTO.ImageFile != null)
                {
                    if (!CloudinaryHelpers.IsImageFile(contestDTO.ImageFile))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Uploaded file is not a valid image. Required .jpeg, .jpg, .png file");
                    }

                    // Upload image and get URL
                    string imageUrl = await _cloudinaryService.UploadFileAsync(contestDTO.ImageFile, CONTEST_IMAGE_FOLDER);
                    existingContest.ImgUrl = imageUrl;
                }

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
                {
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(existingContest.ContestId),
                        DateTimeHelpers.ToIso8601String(contestDTO.RegistrationStart.Value));
                }
                if (contestDTO.RegistrationEnd.HasValue)
                {
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(existingContest.ContestId),
                        DateTimeHelpers.ToIso8601String(contestDTO.RegistrationEnd.Value));
                }

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
                PaginatedList<GetContestDTO> result = await GetPaginatedContestAsync(1, 1, existingContest.ContestId, null, null, null, null, null, null, false, false);

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
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (dto == null)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

                // Validate name
                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Name is required.");

                // Validate year
                int currentYear = DateTime.UtcNow.Year;
                if (dto.Year < currentYear)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, $"Year must be ≥ {currentYear}.");

                // Validate registration date ranges
                if (dto.RegistrationStart.HasValue && dto.RegistrationEnd.HasValue && dto.RegistrationStart.Value >= dto.RegistrationEnd.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration start must be before registration end.");

                // Validate contest date ranges
                if (dto.Start.HasValue && dto.End.HasValue && dto.Start.Value >= dto.End.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest start must be before contest end.");

                // Validate registration dates vs contest dates
                if (dto.RegistrationStart.HasValue && dto.Start.HasValue && dto.RegistrationStart.Value >= dto.Start.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration start must be before contest start.");

                // Validate registration end vs contest start
                if (dto.RegistrationEnd.HasValue && dto.Start.HasValue && dto.RegistrationEnd.Value >= dto.Start.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Registration end must be before contest start.");

                string currentUserId = GetCurrentUserIdOrThrow();

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

                string imageUrl = string.Empty;

                if (dto.ImageFile != null)
                {
                    // Validate image file
                    if (!CloudinaryHelpers.IsImageFile(dto.ImageFile))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Uploaded file is not a valid image. Required .jpeg, .jpg, .png file");
                    }

                    // Upload image and get URL
                    imageUrl = await _cloudinaryService.UploadFileAsync(dto.ImageFile, CONTEST_IMAGE_FOLDER);
                }

                // Map DTO to entity
                Contest entity = _mapper.Map<Contest>(dto);
                entity.ContestId = Guid.NewGuid();
                entity.Name = nameTrim;
                entity.Status = ContestStatusEnum.Draft.ToString();
                entity.CreatedAt = DateTime.UtcNow;
                entity.CreatedBy = currentUserId;
                entity.ImgUrl = imageUrl;

                if (dto.Start.HasValue)
                    entity.Start = dto.Start.Value;
                if (dto.End.HasValue)
                    entity.End = dto.End.Value;

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
                {
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(entity.ContestId),
                        DateTimeHelpers.ToIso8601String(dto.RegistrationStart.Value));
                }
                if (dto.RegistrationEnd.HasValue)
                {
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(entity.ContestId),
                        DateTimeHelpers.ToIso8601String(dto.RegistrationEnd.Value));
                }

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
                created.Start = entity.Start;
                created.End = entity.End;
                created.CreatedAt = entity.CreatedAt;
                created.imageUrl = imageUrl;

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
            IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            IGenericRepository<Attachment> attachmentRepo = _unitOfWork.GetRepository<Attachment>();

            // Fetch the contest with its rounds
            Contest? contest = await contestRepo.Entities
                .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                .Include(c => c.Rounds)
                    .ThenInclude(r => r.Problem)
                        .ThenInclude(p => p.TestCases)
                .FirstOrDefaultAsync();

            // Validate contest existence
            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            // Initialize result DTO
            PublishReadinessDTO result = new PublishReadinessDTO { ContestId = contestId };

            // Get rounds
            List<Round> rounds = contest.Rounds
                .Where(r => !r.DeletedAt.HasValue)
                .ToList();

            // Check if there are any rounds
            if (!rounds.Any())
            {
                result.Missing.Add("No rounds found.");
                result.IsReady = false;
                return result;
            }

            // Extract round IDs
            List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();

            // Check for time limit configuration on all rounds
            List<Config> timeLimitConfigs = await configRepo.Entities
                .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                            && c.Key.Contains("time_limit_seconds")
                            && c.DeletedAt == null)
                .ToListAsync();

            // Create a set of round IDs that have time limit configured
            HashSet<Guid> roundsWithTimeLimit = new HashSet<Guid>();
            foreach (Config config in timeLimitConfigs)
            {
                // Extract round ID from key and validate the value
                if (!string.IsNullOrEmpty(config.Value) && int.TryParse(config.Value, out int timeLimit))
                {
                    foreach (Guid roundId in roundIds)
                    {
                        if (config.Key.Contains(roundId.ToString()))
                        {
                            roundsWithTimeLimit.Add(roundId);
                            break;
                        }
                    }
                }
            }

            // Find rounds without time limit
            List<Round> roundsWithoutTimeLimit = rounds
                .Where(r => !roundsWithTimeLimit.Contains(r.RoundId))
                .ToList();

            // Report rounds missing time limit
            if (roundsWithoutTimeLimit.Any())
            {
                string roundNames = string.Join(", ", roundsWithoutTimeLimit.Select(r => $"'{r.Name}'"));
                result.Missing.Add($"Round(s) {roundNames} missing time limit configuration.");
            }

            // Store Auto Evaluation, Manual problem in problems list
            List<Problem> problems = await problemRepo.Entities
                .Where(p => roundIds.Contains(p.RoundId) && p.DeletedAt == null)
                .Include(p => p.TestCases)
                .ToListAsync();

            // Get MCQ tests
            List<McqTest> mcqTests = await mcqTestRepo.Entities
                .Where(t => roundIds.Contains(t.RoundId) && t.DeletedAt == null)
                .Include(t => t.Round)
                .Include(t => t.McqTestQuestions)
                .ToListAsync();

            // Create a lookup for rounds that have problems or MCQ tests
            HashSet<Guid> roundsWithProblems = problems.Select(p => p.RoundId).ToHashSet();
            HashSet<Guid> roundsWithMcqTests = mcqTests.Select(t => t.RoundId).ToHashSet();

            // Combine both sets to find rounds with any content
            HashSet<Guid> roundsWithContent = roundsWithProblems.Union(roundsWithMcqTests).ToHashSet();

            // Find rounds without any problems or MCQ tests
            List<Round> roundsWithoutContent = rounds
                .Where(r => !roundsWithContent.Contains(r.RoundId))
                .ToList();

            // Report rounds missing content
            if (roundsWithoutContent.Any())
            {
                string roundNames = string.Join(", ", roundsWithoutContent.Select(r => $"'{r.Name}'"));
                result.Missing.Add($"Round(s) {roundNames} missing a problem or MCQ test.");
            }

            // Check if MCQ tests contain questions
            List<McqTest> mcqTestsWithoutQuestions = mcqTests
                .Where(t => !t.McqTestQuestions.Any())
                .ToList();

            if (mcqTestsWithoutQuestions.Any())
            {
                string testInfo = string.Join(", ", mcqTestsWithoutQuestions.Select(t =>
                    $"'{t.Name ?? "Unnamed"}' in round '{t.Round.Name}'"));
                result.Missing.Add($"MCQ test(s) {testInfo} have no questions.");
            }

            // Check if Auto Evaluation problems have rubric
            List<Problem> autoEvaluationProblems = problems
                .Where(p => p.Type == ProblemTypeEnum.AutoEvaluation.ToString())
                .ToList();

            if (autoEvaluationProblems.Any())
            {
                // Check if Auto Evaluation problems have test cases
                List<Problem> autoEvalProblemsWithoutTestCases = problems
                    .Where(p => p.Type == ProblemTypeEnum.AutoEvaluation.ToString()
                                && !p.TestCases.Any(tc => tc.DeletedAt == null))
                    .ToList();

                if (autoEvalProblemsWithoutTestCases.Any())
                {
                    string problemInfo = string.Join(", ", autoEvalProblemsWithoutTestCases.Select(p =>
                    {
                        Round? round = rounds.FirstOrDefault(r => r.RoundId == p.RoundId);
                        return $"'{round?.Name ?? "Unknown Round"}'";
                    }));
                    result.Missing.Add($"Auto-evaluation round(s) {problemInfo} missing test cases.");
                }
            }

            // Check if Manual evaluation problems have rubric
            List<Problem> manualProblems = problems
                .Where(p => p.Type == ProblemTypeEnum.Manual.ToString())
                .ToList();

            if (manualProblems.Any())
            {
                // Get problem IDs for manual evaluation
                List<Guid> manualProblemIds = manualProblems.Select(p => p.ProblemId).ToList();

                // Check for rubric attachments
                List<Problem> manualProblemsWithoutRubrics = problems
                    .Where(p => p.Type == ProblemTypeEnum.Manual.ToString()
                                && !p.TestCases.Any(tc => tc.DeletedAt == null))
                    .ToList();

                if (manualProblemsWithoutRubrics.Any())
                {
                    string problemInfo = string.Join(", ", manualProblemsWithoutRubrics.Select(p =>
                    {
                        Round? round = rounds.FirstOrDefault(r => r.RoundId == p.RoundId);
                        return $"'{round?.Name ?? "Unknown Round"}'";
                    }));
                    result.Missing.Add($"Manual evaluation round(s) {problemInfo} missing rubric.");
                }
            }

            // Check registration window configuration
            string? regStart = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestRegStart(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? regEnd = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestRegEnd(contestId) && c.DeletedAt == null)
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

            // Check for contest image
            if (string.IsNullOrWhiteSpace(contest.ImgUrl))
            {
                result.Missing.Add("Contest image not uploaded.");
            }

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
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Fetch the contest
                Contest? contest = await contestRepo.GetByIdAsync(contestId);

                // Validate contest existence
                if (contest == null || contest.DeletedAt != null)
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

                // Get current time
                DateTime now = DateTime.UtcNow;

                // Fetch registration start from config
                string regStartKey = ConfigKeys.ContestRegStart(contestId);
                Config? regStartConfig = await configRepo.Entities
                    .Where(c => c.Key == regStartKey && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Fetch registration end from config
                string regEndKey = ConfigKeys.ContestRegEnd(contestId);
                Config? regEndConfig = await configRepo.Entities
                    .Where(c => c.Key == regEndKey && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                DateTime? registrationStart = null;
                DateTime? registrationEnd = null;

                if (regStartConfig != null && DateTime.TryParse(regStartConfig.Value, out DateTime regStart))
                {
                    registrationStart = regStart;
                }

                if (regEndConfig != null && DateTime.TryParse(regEndConfig.Value, out DateTime regEnd))
                {
                    registrationEnd = regEnd;
                }

                // Determine contest status based on time and registration windows
                string newStatus;

                // Priority 1: Check if contest has ended (terminal state)
                if (contest.End.HasValue && now >= contest.End.Value)
                {
                    newStatus = ContestStatusEnum.Completed.ToString();
                }
                // Priority 2: Check if contest is ongoing
                else if (contest.Start.HasValue && now >= contest.Start.Value && now < contest.End)
                {
                    newStatus = ContestStatusEnum.Ongoing.ToString();
                }
                // Priority 3: Check if registration has closed but contest hasn't started
                else if (registrationEnd.HasValue && now >= registrationEnd.Value
                    && contest.Start.HasValue && now < contest.Start.Value
                    && contest.Status != ContestStatusEnum.RegistrationClosed.ToString())
                {
                    newStatus = ContestStatusEnum.RegistrationClosed.ToString();
                }
                // Priority 4: Check if registration is open
                else if (registrationStart.HasValue && registrationEnd.HasValue
                    && now >= registrationStart.Value && now < registrationEnd.Value
                    && contest.Status == ContestStatusEnum.Published.ToString())
                {
                    newStatus = ContestStatusEnum.RegistrationOpen.ToString();
                }
                // Default: Published (before registration starts)
                else
                {
                    newStatus = ContestStatusEnum.Published.ToString();
                }

                // Update contest status
                contest.Status = newStatus;
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
                    $"Error publishing Contest: {ex.Message}");
            }
        }

        public async Task<IReadOnlyList<ContestPolicyDTO>> GetContestPoliciesAsync(Guid contestId)
        {
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            string prefix = ConfigKeys.ContestPolicyPrefix(contestId);

            List<Config> configs = await configRepo.Entities
                .Where(c => c.Scope == "contest"
                            && c.DeletedAt == null
                            && c.Key.StartsWith(prefix))
                .ToListAsync();

            var result = configs.Select(c => new ContestPolicyDTO
            {
                Key = ExtractPolicyKeyFromKey(contestId, c.Key),
                Value = c.Value
            }).ToList();

            return result;
        }

        public async Task SetContestPoliciesAsync(Guid contestId, IList<ContestPolicyDTO> policies)
        {
            if (policies == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Policies cannot be null.");

            Contest contest = await GetContestOwnedByCurrentOrganizer(contestId);

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            foreach (var policy in policies)
            {
                if (string.IsNullOrWhiteSpace(policy.Key))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Policy key is required.");

                string normalizedKey = policy.Key.Trim().ToLowerInvariant();
                string configKey = ConfigKeys.ContestPolicy(contest.ContestId, normalizedKey);
                string value = policy.Value?.Trim() ?? string.Empty;

                await UpsertConfigAsync(configRepo, configKey, value);
            }

            await _unitOfWork.SaveAsync();
        }

        public async Task DeleteContestPolicyAsync(Guid contestId, string policyKey)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                if (string.IsNullOrWhiteSpace(policyKey))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Policy key is required.");

                Contest contest = await GetContestOwnedByCurrentOrganizer(contestId);

                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                string normalizedKey = policyKey.Trim().ToLowerInvariant();
                string configKey = ConfigKeys.ContestPolicy(contest.ContestId, normalizedKey);

                // Delete associated teams and their members
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

                // Get all teams associated with the contest
                List<Team> teams = await teamRepo.Entities
                    .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                    .Include(t => t.TeamMembers)
                    .ToListAsync();

                if (teams.Any())
                {
                    DateTime now = DateTime.UtcNow;

                    // Soft delete all team members
                    foreach (Team team in teams)
                    {
                        List<TeamMember> teamMembers = team.TeamMembers.ToList();

                        foreach (TeamMember member in teamMembers)
                        {
                            await teamMemberRepo.DeleteAsync(member);
                        }

                        // Soft delete the team
                        team.DeletedAt = now;
                        await teamRepo.UpdateAsync(team);
                    }
                }

                // Save all changes
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
                    $"Error deleting Contest policy: {ex.Message}");
            }
        }

        private async Task<Contest> GetContestOwnedByCurrentOrganizer(Guid contestId)
        {
            string currentUserId = GetCurrentUserIdOrThrow();

            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            if (!string.Equals(contest.CreatedBy, currentUserId, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only the organizer who created this contest can modify policies.");

            return contest;
        }

        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrWhiteSpace(id))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            return id;
        }

        // parse key "contest:{contestId}:policy:{policyKey}" → policyKey
        private static string ExtractPolicyKeyFromKey(Guid contestId, string key)
        {
            string prefix = ConfigKeys.ContestPolicyPrefix(contestId);
            return key.StartsWith(prefix, StringComparison.Ordinal)
                ? key.Substring(prefix.Length)
                : key;
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
                    $"Error cancelling Contest: {ex.Message}");
            }
        }
    }
}