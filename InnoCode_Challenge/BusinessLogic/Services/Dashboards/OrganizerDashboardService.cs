using BusinessLogic.IServices.Dashboards;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.DashboardDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Dashboards
{
    public class OrganizerDashboardService : IOrganizerDashboardService
    {
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public OrganizerDashboardService(
            IUOW unitOfWork,
            IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<OrganizerDashboardDTO> GetOrganizerDashboardAsync(
            Guid? organizerId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = null)
        {
            try
            {
                // Calculate date range if predefined option is specified
                if (predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom)
                {
                    (DateTime calculatedStart, DateTime calculatedEnd) = DateTimeHelpers.CalculateDateRange(predefined.Value);
                    startDate = calculatedStart;
                    endDate = calculatedEnd;
                }

                // Get organizer ID
                Guid targetOrganizerId = organizerId ?? await GetCurrentOrganizerIdAsync();

                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Build base query for contests created by this organizer
                IQueryable<Contest> contestsQuery = contestRepo.Entities
                    .Where(c => c.CreatedBy == targetOrganizerId.ToString() && c.DeletedAt == null);

                // Apply date filtering if provided
                if (startDate.HasValue)
                {
                    contestsQuery = contestsQuery.Where(c => c.CreatedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    DateTime endOfDay = endDate.Value.Date.AddDays(1);
                    contestsQuery = contestsQuery.Where(c => c.CreatedAt < endOfDay);
                }

                // Sort contests by year and creation date
                List<Contest> contests = await contestsQuery
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                // Initialize dashboard
                OrganizerDashboardDTO dashboard = new OrganizerDashboardDTO
                {
                    TotalContestsCreated = contests.Count,
                    DraftContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Draft.ToString())),
                    ActiveContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Ongoing.ToString()) ||
                        string.Equals(c.Status, ContestStatusEnum.RegistrationOpen.ToString()) ||
                        string.Equals(c.Status, ContestStatusEnum.RegistrationClosed.ToString())),
                    CompletedContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Completed.ToString()))
                };

                if (contests.Any())
                {
                    List<Guid> contestIds = contests.Select(c => c.ContestId).ToList();

                    // Add most active contest and contest with most appeals data
                    dashboard.MostActiveContest = await GetMostActiveContestAsync(contestIds, teamRepo, teamMemberRepo);
                    dashboard.ContestWithMostAppeals = await GetContestWithMostAppealsAsync(contestIds, appealRepo);
                }


                return dashboard;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving organizer dashboard: {ex.Message}");
            }
        }

        public async Task<PaginatedList<ContestSummaryDTO>> GetMyContestsAsync(
            Guid? organizerId = null,
            int pageNumber = 1,
            int pageSize = 10,
            string? status = null,
            string? searchName = null,
            int? year = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            try
            {
                // Validate pagination parameters
                if (pageNumber < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number must be greater than or equal to 1.");
                }

                if (pageSize < 1 || pageSize > 100)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page size must be between 1 and 100.");
                }

                // Get organizer ID
                Guid targetOrganizerId = organizerId ?? await GetCurrentOrganizerIdAsync();

                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Build base query
                IQueryable<Contest> query = contestRepo.Entities
                    .Where(c => c.CreatedBy == targetOrganizerId.ToString() && c.DeletedAt == null);

                // Apply status filter
                if (!string.IsNullOrWhiteSpace(status))
                {
                    string normalizedStatus = status.Trim();
                    query = query.Where(c => c.Status == normalizedStatus);
                }

                // Apply search filter
                if (!string.IsNullOrWhiteSpace(searchName))
                {
                    string searchTerm = searchName.Trim().ToLower();
                    query = query.Where(c => c.Name.ToLower().Contains(searchTerm));
                }

                // Apply year filter
                if (year.HasValue)
                {
                    query = query.Where(c => c.Year == year.Value);
                }

                // Apply date range filter
                if (startDate.HasValue)
                {
                    query = query.Where(c => c.CreatedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    DateTime endOfDay = endDate.Value.Date.AddDays(1);
                    query = query.Where(c => c.CreatedAt < endOfDay);
                }

                // Sort by creation date (newest first)
                query = query.OrderByDescending(c => c.CreatedAt);

                // Get total count before pagination
                int totalCount = await query.CountAsync();

                // Apply pagination
                List<Contest> contests = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // If no contests found, return empty list
                if (!contests.Any())
                {
                    return new PaginatedList<ContestSummaryDTO>(
                        new List<ContestSummaryDTO>(),
                        totalCount,
                        pageNumber,
                        pageSize);
                }

                // Get contest IDs
                List<Guid> contestIds = contests.Select(c => c.ContestId).ToList();

                // Build detailed summaries
                List<ContestSummaryDTO> summaries = await BuildContestSummariesAsync(
                    contests,
                    contestIds,
                    teamRepo,
                    teamMemberRepo,
                    appealRepo,
                    certificateRepo,
                    configRepo);

                // Return paginated result
                return new PaginatedList<ContestSummaryDTO>(
                    summaries,
                    totalCount,
                    pageNumber,
                    pageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving contests list: {ex.Message}");
            }
        }

        public async Task<ContestSummaryDTO> GetContestSummaryAsync(Guid contestId)
        {
            try
            {
                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get contest
                Contest? contest = await contestRepo.Entities
                    .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

                if (contest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Contest not found.");
                }

                // Verify ownership
                Guid currentOrganizerId = await GetCurrentOrganizerIdAsync();
                if (contest.CreatedBy != currentOrganizerId.ToString())
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "You can only view your own contests.");
                }

                // Build summary
                List<ContestSummaryDTO> summaries = await BuildContestSummariesAsync(
                    new List<Contest> { contest },
                    new List<Guid> { contestId },
                    teamRepo,
                    teamMemberRepo,
                    appealRepo,
                    certificateRepo,
                    configRepo);

                return summaries.First();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving contest summary: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the most active contest based on team and student counts
        /// </summary>
        private async Task<ContestSummaryDTO?> GetMostActiveContestAsync(
            List<Guid> contestIds,
            IGenericRepository<Team> teamRepo,
            IGenericRepository<TeamMember> teamMemberRepo)
        {
            if (!contestIds.Any())
                return null;

            // Get team counts per contest
            var teamCounts = await teamRepo.Entities
                .Where(t => contestIds.Contains(t.ContestId) && t.DeletedAt == null)
                .GroupBy(t => t.ContestId)
                .Select(g => new
                {
                    ContestId = g.Key,
                    TeamCount = g.Count(),
                    TeamIds = g.Select(t => t.TeamId).ToList()
                })
                .ToListAsync();

            if (!teamCounts.Any())
                return null;

            // Get student counts for contests with teams
            var studentCountsDict = new Dictionary<Guid, int>();
            foreach (var tc in teamCounts)
            {
                int studentCount = await teamMemberRepo.Entities
                    .Where(tm => tc.TeamIds.Contains(tm.TeamId))
                    .Select(tm => tm.StudentId)
                    .Distinct()
                    .CountAsync();

                studentCountsDict[tc.ContestId] = studentCount;
            }

            // Find the most active contest
            var mostActive = teamCounts
                .OrderByDescending(tc => tc.TeamCount)
                .ThenByDescending(tc => studentCountsDict.GetValueOrDefault(tc.ContestId, 0))
                .First();

            // Get contest entity
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == mostActive.ContestId);

            if (contest == null)
                return null;

            // Build contest summary
            IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
            IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            List<ContestSummaryDTO> summaries = await BuildContestSummariesAsync(
                new List<Contest> { contest },
                new List<Guid> { contest.ContestId },
                teamRepo,
                teamMemberRepo,
                appealRepo,
                certificateRepo,
                configRepo);

            return summaries.FirstOrDefault();
        }

        /// <summary>
        /// Get the contest with the most appeals
        /// </summary>
        private async Task<ContestSummaryDTO?> GetContestWithMostAppealsAsync(
            List<Guid> contestIds,
            IGenericRepository<Appeal> appealRepo)
        {
            if (!contestIds.Any())
                return null;

            // Get appeal counts per contest
            var appealCounts = await appealRepo.Entities
                .Where(a => contestIds.Contains(a.Target.ContestId) && a.DeletedAt == null)
                .GroupBy(a => a.Target.ContestId)
                .Select(g => new
                {
                    ContestId = g.Key,
                    AppealCount = g.Count()
                })
                .OrderByDescending(x => x.AppealCount)
                .FirstOrDefaultAsync();

            if (appealCounts == null || appealCounts.AppealCount == 0)
                return null;

            // Get contest entity
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == appealCounts.ContestId);

            if (contest == null)
                return null;

            // Build contest summary
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
            IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
            IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            List<ContestSummaryDTO> summaries = await BuildContestSummariesAsync(
                new List<Contest> { contest },
                new List<Guid> { contest.ContestId },
                teamRepo,
                teamMemberRepo,
                appealRepo,
                certificateRepo,
                configRepo);

            return summaries.FirstOrDefault();
        }

        /// <summary>
        /// Build contest summaries with all metrics
        /// </summary>
        private async Task<List<ContestSummaryDTO>> BuildContestSummariesAsync(
            List<Contest> contests,
            List<Guid> contestIds,
            IGenericRepository<Team> teamRepo,
            IGenericRepository<TeamMember> teamMemberRepo,
            IGenericRepository<Appeal> appealRepo,
            IGenericRepository<Certificate> certificateRepo,
            IGenericRepository<Config> configRepo)
        {
            if (!contestIds.Any())
                return new List<ContestSummaryDTO>();

            // Get teams per contest
            Dictionary<Guid, List<Team>> teamsByContest = await teamRepo.Entities
                .Where(t => contestIds.Contains(t.ContestId) && t.DeletedAt == null)
                .GroupBy(t => t.ContestId)
                .ToDictionaryAsync(g => g.Key, g => g.ToList());

            // Get student counts per contest
            Dictionary<Guid, int> studentCountByContest = new Dictionary<Guid, int>();
            foreach (var contestId in contestIds)
            {
                if (!teamsByContest.TryGetValue(contestId, out List<Team>? teams))
                {
                    studentCountByContest[contestId] = 0;
                    continue;
                }

                List<Guid> teamIds = teams.Select(t => t.TeamId).ToList();
                int studentCount = await teamMemberRepo.Entities
                    .Where(tm => teamIds.Contains(tm.TeamId))
                    .Select(tm => tm.StudentId)
                    .Distinct()
                    .CountAsync();

                studentCountByContest[contestId] = studentCount;
            }

            // Get appeal counts per contest
            Dictionary<Guid, int> appealCountByContest = await appealRepo.Entities
                .Where(a => contestIds.Contains(a.Target.ContestId) && a.DeletedAt == null)
                .GroupBy(a => a.Target.ContestId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Get certificate counts per contest
            Dictionary<Guid, int> certificateCountByContest = await certificateRepo.Entities
                .Where(c => teamsByContest.Keys.Contains(c.Team!.ContestId) && c.DeletedAt == null)
                .GroupBy(c => c.Team!.ContestId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Get config data for each contest
            Dictionary<Guid, (DateTime? RegStart, DateTime? RegEnd)> contestConfigs =
                await GetContestConfigsAsync(contestIds, configRepo);

            // Build summaries (preserving the input order)
            List<ContestSummaryDTO> summaries = new List<ContestSummaryDTO>();

            foreach (Contest contest in contests)
            {
                List<Team>? teams = teamsByContest.GetValueOrDefault(contest.ContestId, new List<Team>());

                ContestSummaryDTO summary = new ContestSummaryDTO
                {
                    ContestId = contest.ContestId,
                    ContestName = contest.Name,
                    Status = contest.Status,
                    Year = contest.Year,
                    TotalTeams = teams.Count,
                    TotalStudents = studentCountByContest.GetValueOrDefault(contest.ContestId, 0),
                    TotalAppeals = appealCountByContest.GetValueOrDefault(contest.ContestId, 0),
                    CertificatesIssued = certificateCountByContest.GetValueOrDefault(contest.ContestId, 0),
                    TeamStatusBreakdown = new TeamStatusBreakdownDTO
                    {
                        ActiveTeams = teams.Count(t =>
                            string.Equals(t.Status, TeamStatusConstants.Active)),
                        EliminatedTeams = teams.Count(t =>
                            string.Equals(t.Status, TeamStatusConstants.Eliminated)),
                        DisqualifiedTeams = teams.Count(t =>
                            string.Equals(t.Status, TeamStatusConstants.Disqualified)),
                        CompletedTeams = 0
                    },
                    ContestStart = contest.Start,
                    ContestEnd = contest.End
                };

                // Add config data
                if (contestConfigs.TryGetValue(contest.ContestId, out var config))
                {
                    summary.RegistrationStart = config.RegStart;
                    summary.RegistrationEnd = config.RegEnd;
                }

                // Calculate progress percentage
                summary.ProgressPercentage = CalculateContestProgress(
                    summary.RegistrationStart,
                    summary.RegistrationEnd,
                    summary.ContestStart,
                    summary.ContestEnd);

                summaries.Add(summary);
            }

            return summaries;
        }

        /// <summary>
        /// Get contest registration dates from config
        /// </summary>
        private async Task<Dictionary<Guid, (DateTime? RegStart, DateTime? RegEnd)>> GetContestConfigsAsync(
            List<Guid> contestIds,
            IGenericRepository<Config> configRepo)
        {
            Dictionary<Guid, (DateTime? RegStart, DateTime? RegEnd)> result = new();

            foreach (Guid contestId in contestIds)
            {
                string regStartKey = ConfigKeys.ContestRegStart(contestId);
                string regEndKey = ConfigKeys.ContestRegEnd(contestId);

                string? regStartValue = await configRepo.Entities
                    .Where(c => c.Key == regStartKey && c.DeletedAt == null)
                    .Select(c => c.Value)
                    .FirstOrDefaultAsync();

                string? regEndValue = await configRepo.Entities
                    .Where(c => c.Key == regEndKey && c.DeletedAt == null)
                    .Select(c => c.Value)
                    .FirstOrDefaultAsync();

                DateTime? regStart = ParseDateTimeOrNull(regStartValue);
                DateTime? regEnd = ParseDateTimeOrNull(regEndValue);

                result[contestId] = (regStart, regEnd);
            }

            return result;
        }

        /// <summary>
        /// Calculate contest progress percentage (0-100)
        /// </summary>
        private double CalculateContestProgress(
            DateTime? regStart,
            DateTime? regEnd,
            DateTime? contestStart,
            DateTime? contestEnd)
        {
            if (!regStart.HasValue || !contestEnd.HasValue)
                return 0;

            DateTime now = DateTime.UtcNow;

            // Before registration starts
            if (now < regStart.Value)
                return 0;

            // After contest ends
            if (contestEnd.HasValue && now >= contestEnd.Value)
                return 100;

            // Calculate progress
            TimeSpan totalDuration = contestEnd.Value - regStart.Value;
            TimeSpan elapsed = now - regStart.Value;

            double progress = Math.Round((elapsed.TotalSeconds / totalDuration.TotalSeconds) * 100,2);
            return Math.Min(Math.Max(progress, 0), 100);
        }

        /// <summary>
        /// Parse DateTime from string or return null
        /// </summary>
        private DateTime? ParseDateTimeOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, out DateTime result))
                return result.ToUniversalTime();

            return null;
        }

        /// <summary>
        /// Get current organizer ID from JWT token
        /// </summary>
        private async Task<Guid> GetCurrentOrganizerIdAsync()
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ErrorException(StatusCodes.Status401Unauthorized,
                    ResponseCodeConstants.UNAUTHORIZED,
                    "User not authenticated.");
            }

            if (!Guid.TryParse(userId, out Guid organizerId))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Invalid user ID format.");
            }

            // Verify user is an organizer
            string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

            if (!string.Equals(userRole, RoleConstants.ContestOrganizer, StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Only organizers can access this dashboard.");
            }

            return organizerId;
        }
    }
}