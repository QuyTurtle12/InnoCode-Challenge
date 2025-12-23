using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Hangfire;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using System;
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
        private readonly INotificationService _notificationService;
        private readonly IActivityLogWriter _activityLogWriter;
        private readonly ILogger<ContestService> _logger;

        private const int MIN_YEAR = 10;
        private const string CONTEST_IMAGE_FOLDER = "contest_images";

        public ContestService(
            IMapper mapper,
            IUOW uow,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            INotificationService notificationService,
            IActivityLogWriter activityLogWriter,
            ILogger<ContestService> logger) 
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
            _activityLogWriter = activityLogWriter;
            _logger = logger; 
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


                // Log contest cancellation activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestCancel, TargetTypes.Contest, id.ToString());

                // Notify participants about contest cancellation
                await SafeNotifyParticipantsAsync(id, NotificationTypes.ContestCancelled, new
                {
                    contestId = id,
                    targetType = TargetTypes.Contest,
                    targetId = id.ToString(),
                    message = "This contest has been cancelled."
                });

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

                string? userRole = _httpContextAccessor.HttpContext?.User?
                        .FindFirstValue(ClaimTypes.Role);

                // Get contests where the current logged-in student is a participant
                if (isMyParticipatedContest)
                {
                    // Get current user ID from HttpContext
                    string? userId = _httpContextAccessor.HttpContext?.User?
                        .FindFirstValue(ClaimTypes.NameIdentifier);

                    // If user ID is available, get the corresponding student ID
                    if (!string.IsNullOrEmpty(userId))
                    {
                        if (userRole == RoleConstants.Student)
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

                        if (userRole == RoleConstants.Mentor)
                        {
                            // Get mentor repository
                            IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();

                            // Find the mentor ID associated with the user ID
                            Guid? mentorId = await mentorRepo.Entities
                                .Where(m => m.UserId.ToString() == userId && m.DeletedAt == null)
                                .Select(m => m.MentorId)
                                .FirstOrDefaultAsync();

                            // If mentor ID is found, filter contests accordingly
                            if (mentorId.HasValue)
                            {
                                // Include Teams and Mentors for filtering
                                query = query.Include(c => c.Teams);

                                // Filter contests where mentor is in a team
                                query = query.Where(c => c.Teams.Any(t =>
                                    t.MentorId == mentorId.Value
                                 && t.DeletedAt == null));
                            }
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

                // For non-organizers/admins/staff, don't show draft contests
                if (userRole != RoleConstants.ContestOrganizer && userRole != RoleConstants.Admin && userRole != RoleConstants.Staff)
                {
                    query = query.Where(c => c.Status != ContestStatusEnum.Draft.ToString());
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

                    // Fetch team members min from config
                    string teamMemberMinKey = ConfigKeys.ContestTeamMembersMin(item.ContestId);
                    Config? teamMemberMinConfig = configLookup[teamMemberMinKey].FirstOrDefault();
                    if (teamMemberMinConfig != null && int.TryParse(teamMemberMinConfig.Value, out int teamMemberMin))
                    {
                        contestDTO.TeamMembersMin = teamMemberMin;
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
                // Fetch team members min from config
                string teamMemberMinKey = ConfigKeys.ContestTeamMembersMin(contest.ContestId);
                Config? teamMemberMinConfig = configLookup[teamMemberMinKey].FirstOrDefault();
                if (teamMemberMinConfig != null && int.TryParse(teamMemberMinConfig.Value, out int teamMemberMin))
                {
                    contestDTO.TeamMembersMin = teamMemberMin;
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

                // Validate Team Members Min is positive
                if (contestDTO.TeamMembersMin.HasValue && contestDTO.TeamMembersMin.Value < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team members minimum must be at least 1.");

                // Validate Team Members Max is positive
                if (contestDTO.TeamMembersMax.HasValue && contestDTO.TeamMembersMax.Value < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team members maximum must be at least 1.");

                // Validate TeamMembersMin vs TeamMembersMax
                if (contestDTO.TeamMembersMin.HasValue && contestDTO.TeamMembersMax.HasValue && contestDTO.TeamMembersMin.Value > contestDTO.TeamMembersMax.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team members minimum cannot be greater than team members maximum.");

                // Validate Team Limit Max is positive
                if (contestDTO.TeamLimitMax.HasValue && contestDTO.TeamLimitMax.Value < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team limit maximum must be at least 1.");

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

                // Store old values for notification
                var oldStart = existingContest.Start;
                var oldEnd = existingContest.End;
                var oldName = existingContest.Name;
                var oldStatus = existingContest.Status;

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
                int teamMembersMin = contestDTO.TeamMembersMin
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMin, 1);
                int teamMembersMax = contestDTO.TeamMembersMax
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4);
                ValidateTeamMemberRange(teamMembersMin, teamMembersMax);

                int? teamLimitMax = contestDTO.TeamLimitMax
                                     ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);

                // Insert or update config entries
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMin(existingContest.ContestId), teamMembersMin.ToString());
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

                // Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestUpdate, TargetTypes.Contest, existingContest.ContestId.ToString());

                // Notify participants about the update if the contest is in relevant status
                if (existingContest.Status == ContestStatusEnum.Published.ToString()
                    || existingContest.Status == ContestStatusEnum.RegistrationOpen.ToString()
                    || existingContest.Status == ContestStatusEnum.RegistrationClosed.ToString()
                    || existingContest.Status == ContestStatusEnum.Ongoing.ToString())
                {
                    await SafeNotifyParticipantsAsync(existingContest.ContestId, NotificationTypes.ContestUpdated, new
                    {
                        contestId = existingContest.ContestId,
                        name = existingContest.Name,
                        oldName,
                        oldStart,
                        oldEnd,
                        newStart = existingContest.Start,
                        newEnd = existingContest.End,
                        targetType = TargetTypes.Contest,
                        targetId = existingContest.ContestId.ToString(),
                        message = $"Contest '{existingContest.Name}' has been updated."
                    });
                }

                // Return the updated contest DTO
                PaginatedList<GetContestDTO> result = await GetPaginatedContestAsync(1, 1, existingContest.ContestId, null, null, null, null, null, null, false, false);

                // Schedule state transitions using Hangfire
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(existingContest.ContestId)),
                    "ScheduleContestStateTransitionsAsync");


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

                // Validate Team Members Min is positive
                if (dto.TeamMembersMin.HasValue && dto.TeamMembersMin.Value < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team members minimum must be at least 1.");

                // Validate Team Members Max is positive
                if (dto.TeamMembersMax.HasValue && dto.TeamMembersMax.Value < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team members maximum must be at least 1.");

                // Validate TeamMembersMin vs TeamMembersMax
                if (dto.TeamMembersMin.HasValue && dto.TeamMembersMax.HasValue && dto.TeamMembersMin.Value > dto.TeamMembersMax.Value)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team members minimum cannot be greater than team members maximum.");
                
                // Validate Team Limit Max is positive
                if (dto.TeamLimitMax.HasValue && dto.TeamLimitMax.Value < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Team limit maximum must be at least 1.");

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
                int teamMembersMin = dto.TeamMembersMin
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMin, 1);

                int teamMembersMax = dto.TeamMembersMax
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4);
                int? teamLimitMax = dto.TeamLimitMax
                                     ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);

                // Insert or update config entries
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMin(entity.ContestId), teamMembersMin.ToString());
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

                // Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestCreate, TargetTypes.Contest, entity.ContestId.ToString());

                // Map to created DTO
                ContestCreatedDTO created = _mapper.Map<ContestCreatedDTO>(entity);
                created.TeamMembersMin = teamMembersMin;
                created.TeamMembersMax = teamMembersMax;
                created.TeamLimitMax = teamLimitMax;
                created.TeamMembersMin = teamMembersMin;
                created.RewardsText = dto.RewardsText;
                created.RegistrationStart = dto.RegistrationStart;
                created.RegistrationEnd = dto.RegistrationEnd;
                created.Start = entity.Start;
                created.End = entity.End;
                created.CreatedAt = entity.CreatedAt;
                created.imageUrl = imageUrl;

                // Schedule state transitions using Hangfire
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(entity.ContestId)),
                    "ScheduleContestStateTransitionsAsync");

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
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Fetch the contest with its rounds
            Contest? contest = await contestRepo.Entities
                .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                .Include(c => c.Rounds)
                    .ThenInclude(r => r.Problem)
                        .ThenInclude(p => p!.TestCases)
                .FirstOrDefaultAsync();

            // Validate contest existence
            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            // Initialize result DTO
            PublishReadinessDTO result = new PublishReadinessDTO { ContestId = contestId };

            // Get non-deleted rounds
            List<Round> rounds = contest.Rounds.Where(r => !r.DeletedAt.HasValue).ToList();

            // Check if there are any rounds
            if (!rounds.Any())
            {
                result.Missing.Add("No rounds found.");
                result.IsReady = false;
                return result;
            }

            // Extract round IDs
            List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();

            // Validate time limits
            await ValidateRoundTimeLimitsAsync(configRepo, rounds, roundIds, result);

            // Get problems and MCQ tests
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
            IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

            List<Problem> problems = await problemRepo.Entities
                .Where(p => roundIds.Contains(p.RoundId) && p.DeletedAt == null)
                .Include(p => p.TestCases)
                .ToListAsync();

            List<McqTest> mcqTests = await mcqTestRepo.Entities
                .Where(t => roundIds.Contains(t.RoundId) && t.DeletedAt == null)
                .Include(t => t.Round)
                .Include(t => t.McqTestQuestions)
                .ToListAsync();

            // Validate retake round weights
            await ValidateRetakeRoundWeightsAsync(rounds, problems, mcqTests, result);

            // Validate round content
            ValidateRoundContent(rounds, problems, mcqTests, result);

            // Validate MCQ tests
            ValidateMcqTests(mcqTests, result);

            // Validate auto-evaluation problems
            ValidateAutoEvaluationProblems(problems, rounds, result);

            // Validate manual problems and judges
            await ValidateManualProblemsAsync(contestId, configRepo, problems, rounds, result);

            // Validate contest configuration
            await ValidateContestConfigurationAsync(contestId, configRepo, result);

            // Validate contest image
            if (string.IsNullOrWhiteSpace(contest.ImgUrl))
            {
                result.Missing.Add("Contest image not uploaded.");
            }

            // Final readiness determination
            result.IsReady = result.Missing.Count == 0;
            return result;
        }

        private static async Task ValidateRoundTimeLimitsAsync(
            IGenericRepository<Config> configRepo,
            List<Round> rounds,
            List<Guid> roundIds,
            PublishReadinessDTO result)
        {
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

            if (roundsWithoutTimeLimit.Any())
            {
                string roundNames = string.Join(", ", roundsWithoutTimeLimit.Select(r => $"'{r.Name}'"));
                result.Missing.Add($"Round(s) {roundNames} missing time limit configuration.");
            }
        }

        private async Task ValidateRetakeRoundWeightsAsync(
            List<Round> rounds,
            List<Problem> problems,
            List<McqTest> mcqTests,
            PublishReadinessDTO result)
        {
            List<Round> retakeRounds = rounds.Where(r => r.IsRetakeRound && r.MainRoundId.HasValue).ToList();
            if (!retakeRounds.Any())
                return;

            // Get repositories for weight calculation
            IGenericRepository<McqTestQuestion> mcqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();
            IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

            // Load all weights
            List<Guid> mcqTestIds = mcqTests.Select(t => t.TestId).ToList();
            List<McqTestQuestion> allMcqTestQuestions = await mcqTestQuestionRepo.Entities
                .Where(q => mcqTestIds.Contains(q.TestId))
                .ToListAsync();

            List<Guid> problemIds = problems.Select(p => p.ProblemId).ToList();
            List<TestCase> allTestCases = await testCaseRepo.Entities
                .Where(tc => problemIds.Contains(tc.ProblemId) && tc.DeletedAt == null)
                .ToListAsync();

            // Calculate weights for each round
            Dictionary<Guid, double> roundWeights = CalculateRoundWeights(rounds, problems, mcqTests, allMcqTestQuestions, allTestCases);

            // Validate each retake round against its main round
            foreach (Round retakeRound in retakeRounds)
            {
                if (!retakeRound.MainRoundId.HasValue)
                    continue;

                double retakeWeight = roundWeights.GetValueOrDefault(retakeRound.RoundId, 0);
                double mainWeight = roundWeights.GetValueOrDefault(retakeRound.MainRoundId.Value, 0);

                Round? mainRound = rounds.FirstOrDefault(r => r.RoundId == retakeRound.MainRoundId);
                string mainRoundName = mainRound?.Name ?? "Unknown";

                // Validate weights
                if (retakeWeight <= 0)
                {
                    result.Missing.Add($"Retake round '{retakeRound.Name}' has no weight (missing questions/test cases/criteria).");
                }
                else if (mainWeight <= 0)
                {
                    result.Missing.Add($"Main round '{mainRoundName}' for retake round '{retakeRound.Name}' has no weight (missing questions/test cases/criteria).");
                }
                else if (Math.Abs(retakeWeight - mainWeight) > 0.0001)
                {
                    result.Missing.Add($"Retake round '{retakeRound.Name}' total weight ({retakeWeight}) does not match its main round '{mainRoundName}' weight ({mainWeight}).");
                }
            }
        }

        private static Dictionary<Guid, double> CalculateRoundWeights(
            List<Round> rounds,
            List<Problem> problems,
            List<McqTest> mcqTests,
            List<McqTestQuestion> allMcqTestQuestions,
            List<TestCase> allTestCases)
        {
            Dictionary<Guid, double> roundWeights = new Dictionary<Guid, double>();

            foreach (Round round in rounds)
            {
                double totalWeight = 0;

                // Check if round has MCQ test
                McqTest? mcqTest = mcqTests.FirstOrDefault(t => t.RoundId == round.RoundId);
                if (mcqTest != null)
                {
                    totalWeight = allMcqTestQuestions
                        .Where(q => q.TestId == mcqTest.TestId)
                        .Sum(q => q.Weight);
                }
                else
                {
                    // Check if round has problem (auto-evaluation or manual)
                    Problem? problem = problems.FirstOrDefault(p => p.RoundId == round.RoundId);
                    if (problem != null)
                    {
                        totalWeight = allTestCases
                            .Where(tc => tc.ProblemId == problem.ProblemId)
                            .Sum(tc => tc.Weight);
                    }
                }

                roundWeights[round.RoundId] = totalWeight;
            }

            return roundWeights;
        }

        private static void ValidateRoundContent(
            List<Round> rounds,
            List<Problem> problems,
            List<McqTest> mcqTests,
            PublishReadinessDTO result)
        {
            HashSet<Guid> roundsWithProblems = problems.Select(p => p.RoundId).ToHashSet();
            HashSet<Guid> roundsWithMcqTests = mcqTests.Select(t => t.RoundId).ToHashSet();
            HashSet<Guid> roundsWithContent = roundsWithProblems.Union(roundsWithMcqTests).ToHashSet();

            List<Round> roundsWithoutContent = rounds
                .Where(r => !roundsWithContent.Contains(r.RoundId))
                .ToList();

            if (roundsWithoutContent.Any())
            {
                string roundNames = string.Join(", ", roundsWithoutContent.Select(r => $"'{r.Name}'"));
                result.Missing.Add($"Round(s) {roundNames} missing a problem or MCQ test.");
            }
        }

        private static void ValidateMcqTests(List<McqTest> mcqTests, PublishReadinessDTO result)
        {
            List<McqTest> mcqTestsWithoutQuestions = mcqTests
                .Where(t => !t.McqTestQuestions.Any())
                .ToList();

            if (mcqTestsWithoutQuestions.Any())
            {
                string testInfo = string.Join(", ", mcqTestsWithoutQuestions.Select(t =>
                    $"'{t.Name ?? "Unnamed"}' in round '{t.Round.Name}'"));
                result.Missing.Add($"MCQ test(s) {testInfo} have no questions.");
            }
        }

        private static void ValidateAutoEvaluationProblems(
            List<Problem> problems,
            List<Round> rounds,
            PublishReadinessDTO result)
        {
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

        private static async Task ValidateManualProblemsAsync(
            Guid contestId,
            IGenericRepository<Config> configRepo,
            List<Problem> problems,
            List<Round> rounds,
            PublishReadinessDTO result)
        {
            List<Problem> manualProblems = problems
                .Where(p => p.Type == ProblemTypeEnum.Manual.ToString())
                .ToList();

            if (!manualProblems.Any())
                return;

            // Check for rubrics (test cases)
            List<Problem> manualProblemsWithoutRubrics = manualProblems
                .Where(p => !p.TestCases.Any(tc => tc.DeletedAt == null))
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

            // Check for judges
            string contestJudgeKey = $"contest:{contestId}:judge:";
            int judgeCount = await configRepo.Entities
                .Where(c => c.Key.StartsWith(contestJudgeKey) && c.DeletedAt == null)
                .CountAsync();

            if (judgeCount == 0)
            {
                result.Missing.Add("No judges assigned to the contest for manual problems.");
            }
        }

        private static async Task ValidateContestConfigurationAsync(
            Guid contestId,
            IGenericRepository<Config> configRepo,
            PublishReadinessDTO result)
        {
            // Check registration window
            string? regStart = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestRegStart(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? regEnd = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestRegEnd(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(regStart) || string.IsNullOrEmpty(regEnd))
                result.Missing.Add("Registration window not configured.");

            // Check team members configuration
            string? membersMaxContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMax(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? membersMaxDefault = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMax && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(membersMaxContest) && string.IsNullOrEmpty(membersMaxDefault))
                result.Missing.Add("Team members max not configured (contest or global).");

            string? membersMinContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMin(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? membersMinDefault = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMin && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(membersMinContest) && string.IsNullOrEmpty(membersMinDefault))
                result.Missing.Add("Team members min not configured (contest or global).");
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
                else if (contest.Start.HasValue && contest.End.HasValue && now >= contest.Start.Value && now < contest.End.Value)
                {
                    newStatus = ContestStatusEnum.Ongoing.ToString();
                }
                // Priority 3: Check if registration has closed but contest hasn't started
                else if (registrationEnd.HasValue && now >= registrationEnd.Value
                    && contest.Start.HasValue && now < contest.Start.Value)
                {
                    // Check if contest has at least 1 team
                    bool hasTeams = await CheckContestHasTeamsAsync(contestId);

                    if (!hasTeams)
                    {
                        // Set status to Delayed if no teams registered
                        newStatus = ContestStatusEnum.Delayed.ToString();

                        // Delete all scheduled jobs for this contest and its rounds
                        await DeleteContestScheduledJobsAsync(contestId);
                    }
                    else if (contest.Status != ContestStatusEnum.RegistrationClosed.ToString())
                    {
                        newStatus = ContestStatusEnum.RegistrationClosed.ToString();
                    }
                    else
                    {
                        // Keep current status if already RegistrationClosed
                        newStatus = contest.Status;
                    }
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

                // Update contest status only if not already Delayed
                contest.Status = newStatus;
                await contestRepo.UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                // Schedule state transitions only if not moving to Delayed status
                if (newStatus != ContestStatusEnum.Delayed.ToString())
                {
                    SafeEnqueue(() =>
                        BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contestId)),
                        "ScheduleContestStateTransitionsAsync");
                }

                //  Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestPublish, TargetTypes.Contest, contestId.ToString());

                // Notify participants if contest was delayed
                if (newStatus == ContestStatusEnum.Delayed.ToString())
                {
                    await SafeNotifyParticipantsAsync(contestId, NotificationTypes.ContestDelayed, new
                    {
                        contestId,
                        targetType = TargetTypes.Contest,
                        targetId = contestId.ToString(),
                        message = "Contest has been delayed due to insufficient team registrations."
                    });
                }
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

        private async Task<bool> CheckContestHasTeamsAsync(Guid contestId)
        {
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Check if contest has at least 1 non-deleted team
            int teamCount = await teamRepo.Entities
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .CountAsync();

            return teamCount > 0;
        }

        private async Task DeleteContestScheduledJobsAsync(Guid contestId)
        {
            try
            {
                // Get all rounds for the contest
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                List<Guid> roundIds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && r.DeletedAt == null)
                    .Select(r => r.RoundId)
                    .ToListAsync();

                // Delete contest state transition jobs
                string contestJobId = $"contest-state-{contestId}";
                BackgroundJob.Delete(contestJobId);

                // Delete round state transition jobs
                foreach (Guid roundId in roundIds)
                {
                    string roundJobId = $"round-state-{roundId}";
                    BackgroundJob.Delete(roundJobId);
                }

                _logger.LogInformation("Deleted scheduled jobs for contest {ContestId} and {RoundCount} rounds",
                    contestId, roundIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete scheduled jobs for contest {ContestId}", contestId);
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

                // Notify activity log
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestCancel, TargetTypes.Contest, existingContest.ContestId.ToString());

                // Notify participants
                await SafeNotifyParticipantsAsync(existingContest.ContestId, NotificationTypes.ContestCancelled, new
                {
                    contestId = existingContest.ContestId,
                    targetType = TargetTypes.Contest,
                    targetId = existingContest.ContestId.ToString(),
                    message = "This contest has been cancelled."
                });

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

        public async Task<GetContestDTO> StartContestNowAsync(Guid contestId)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                if (contestId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");

                Contest contest = await GetContestOwnedByCurrentOrganizer(contestId);

                DateTime now = DateTime.UtcNow;

                // Prevent invalid timeline if End already passed
                if (contest.End.HasValue && contest.End.Value <= now)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Contest already ended. Cannot start now.");

                contest.Start = now;

                // Update status
                contest.Status = ContestStatusEnum.Ongoing.ToString();

                await _unitOfWork.GetRepository<Contest>().UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                // Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestStartNow, TargetTypes.Contest, contest.ContestId.ToString());

                // Notify participants
                await SafeNotifyParticipantsAsync(contest.ContestId, NotificationTypes.ContestStarted, new
                {
                    contestId = contest.ContestId,
                    name = contest.Name,
                    targetType = TargetTypes.Contest,
                    targetId = contest.ContestId.ToString(),
                    message = $"Contest '{contest.Name}' has started."
                });

                // Schedule state transitions
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contest.ContestId)),
                    "ScheduleContestStateTransitionsAsync");


                return await GetContestByIdAsync(contest.ContestId);
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }

        public async Task<GetContestDTO> EndContestNowAsync(Guid contestId)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                if (contestId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");

                Contest contest = await GetContestOwnedByCurrentOrganizer(contestId);

                DateTime now = DateTime.UtcNow;

                // Must not end before start
                if (contest.Start.HasValue && contest.Start.Value >= now)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Contest has not started yet (start time is in the future).");

                // Must not contest end < any existing round end
                var roundRepo = _unitOfWork.GetRepository<Round>();
                var violatingRounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && r.DeletedAt == null && r.End > now)
                    .Select(r => r.Name)
                    .ToListAsync();

                if (violatingRounds.Any())
                {
                    var ex = new CoreException("END_BLOCKED", "Cannot end contest now because some rounds end after now.", StatusCodes.Status409Conflict)
                    {
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["rounds"] = violatingRounds
                        }
                    };
                    throw ex;
                }

                contest.End = now;
                contest.Status = ContestStatusEnum.Completed.ToString();

                await _unitOfWork.GetRepository<Contest>().UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                // Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestEndNow, TargetTypes.Contest, contest.ContestId.ToString());

                // Notify participants
                await SafeNotifyParticipantsAsync(contest.ContestId, NotificationTypes.ContestEnded, new
                {
                    contestId = contest.ContestId,
                    name = contest.Name,
                    targetType = TargetTypes.Contest,
                    targetId = contest.ContestId.ToString(),
                    message = $"Contest '{contest.Name}' has ended."
                });

                // Schedule state transitions
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contest.ContestId)),
                    "ScheduleContestStateTransitionsAsync");

                return await GetContestByIdAsync(contest.ContestId);
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }


        private static void ValidateTeamMemberRange(int min, int max)
        {
            if (min < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "TeamMembersMin must be >= 1.");

            if (max < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "TeamMembersMax must be >= 1.");

            if (max < min)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "TeamMembersMax must be >= TeamMembersMin.");
        }

        // Get current user ID or throw if unauthenticated/invalid
        private Guid GetCurrentUserGuidOrThrow()
        {
            var idStr = GetCurrentUserIdOrThrow();
            if (!Guid.TryParse(idStr, out var id) || id == Guid.Empty)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user id.");
            return id;
        }

        //  Get user IDs of all participants (students and mentors) in the contest
        private async Task<List<Guid>> GetParticipantUserIdsAsync(Guid contestId)
        {
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var studentIds = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .SelectMany(t => t.TeamMembers
                .Select(tm => tm.Student.UserId))
                .ToListAsync();

            var mentorIds = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null && t.MentorId != null)
                .Select(t => t.Mentor.UserId)
                .ToListAsync();

            return studentIds.Concat(mentorIds)
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();
        }

        // Notify participants of an event
        private async Task TryNotifyParticipantsAsync(Guid contestId, string type, object payload)
        {
            var ids = await GetParticipantUserIdsAsync(contestId);
            if (ids.Count == 0) return;
            await _notificationService.CreateInAppToUsersAsync(ids, type, payload);
        }

        // Write activity log safely, logging any exceptions
        private async Task SafeWriteActivityAsync(Guid actorId, string action, string targetType, string targetId)
        {
            try
            {
                await _activityLogWriter.TryWriteAsync(actorId, action, targetType, targetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Activity log failed. Action={Action}, Target={TargetType}, TargetId={TargetId}, ActorId={ActorId}",
                    action, targetType, targetId, actorId);
            }
        }

        // Notify participants safely, logging any exceptions
        private async Task SafeNotifyParticipantsAsync(Guid contestId, string type, object payload)
        {
            try
            {
                await TryNotifyParticipantsAsync(contestId, type, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notify participants failed. ContestId={ContestId}, Type={Type}", contestId, type);
            }
        }

        // Enqueue a Hangfire job safely, logging any exceptions    
        private void SafeEnqueue(Action enqueue, string jobName)
        {
            try { enqueue(); }
            catch (Exception ex) { _logger.LogError(ex, "Hangfire enqueue failed: {JobName}", jobName); }
        }

    }
}