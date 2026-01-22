using BusinessLogic.IServices.Dashboards;
using DataAccess.Entities;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.DashboardDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;

namespace BusinessLogic.Services.Dashboards
{
    public class DashboardService : IDashboardService
    {
        private readonly IUOW _unitOfWork;

        private const int DEFAULT_TOP_COUNT = 3;
        private const int DEFAULT_TOP_SCHOOL_COUNT = 5;

        private static readonly string[] ValidStatuses = new[]
        {
            nameof(ContestStatusEnum.Published),
            nameof(ContestStatusEnum.RegistrationOpen),
            nameof(ContestStatusEnum.RegistrationClosed),
            nameof(ContestStatusEnum.Ongoing),
            nameof(ContestStatusEnum.Paused),
            nameof(ContestStatusEnum.Completed),
            nameof(ContestStatusEnum.Delayed)
        };

        public DashboardService(
            IUOW unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<DashboardMetricsDTO> GetDashboardMetricsAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined)
        {
            // Calculate date range if predefined option is specified
            bool usePredefinedRange = predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom;

            // Calculate date range if predefined option is specified
            if (usePredefinedRange)
            {
                (DateTime calculatedStart, DateTime calculatedEnd) = CalculateDateRange(predefined.Value);
                startDate = calculatedStart;
                endDate = calculatedEnd;
            }

            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            IQueryable<Contest> contestQuery = BuildContestQuery(contestRepo, startDate, endDate);

            // Get contest status breakdown
            ContestStatusBreakdownDTO statusBreakdown = await GetStatusBreakdownAsync(contestQuery);

            // Get valid contest IDs
            List<Guid> validContestIds = await GetValidContestIdsAsync(contestQuery);

            // Get total teams and students
            int totalTeams = await GetTotalTeamsAsync(teamRepo, validContestIds);
            int totalStudents = await GetTotalStudentsAsync(teamRepo, validContestIds);

            // Calculate growth rate
            (double growthRate, int lastMonthContests) = await CalculateGrowthRateAsync(contestRepo);

            // Calculate total contests
            int totalContests = statusBreakdown.TotalValidContests +
                                statusBreakdown.Draft +
                                statusBreakdown.Cancelled;

            // Prepare final metrics DTO
            DashboardMetricsDTO metrics = new DashboardMetricsDTO
            {
                TotalContests = totalContests,
                TotalTeams = totalTeams,
                TotalStudents = totalStudents,
                StatusBreakdown = statusBreakdown,
                ContestGrowthRate = growthRate,
                NewContestsLastMonth = lastMonthContests
            };

            return metrics;
        }

        public async Task<ChartDataDTO> GetChartDataAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined)
        {
            // Calculate date range if predefined option is specified
            bool usePredefinedRange = predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom;

            if (usePredefinedRange)
            {
                (DateTime calculatedStart, DateTime calculatedEnd) = CalculateDateRange(predefined.Value);
                startDate = calculatedStart;
                endDate = calculatedEnd;
            }

            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            IQueryable<Contest> contestQuery = BuildContestQuery(contestRepo, startDate, endDate);

            List<TrendDataPoint> contestTrend = await GetContestCreationTrendAsync(contestQuery);
            List<TrendDataPoint> teamTrend = await GetTeamRegistrationTrendAsync(teamRepo, startDate, endDate);
            Dictionary<string, int> statusDistribution = await GetContestStatusDistributionAsync(contestQuery);

            // Merge trends and fill gaps
            List<TrendDataPoint> mergedTrend = usePredefinedRange
                ? FillMonthGapsInTrend(contestTrend, teamTrend, startDate!.Value, endDate!.Value)
                : MergeTrendData(contestTrend, teamTrend);

            ChartDataDTO chartData = new ChartDataDTO
            {
                Labels = mergedTrend.Select(x => x.Label).ToList(),
                ContestCreationTrend = mergedTrend.Select(x => x.ContestCount).ToList(),
                TeamRegistrationTrend = mergedTrend.Select(x => x.TeamCount).ToList(),
                ContestsByStatus = statusDistribution
            };

            return chartData;
        }

        public async Task<TopPerformersDTO> GetTopPerformersAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined,
            int topCount = DEFAULT_TOP_COUNT)
        {
            // Calculate date range if predefined option is specified
            if (predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom)
            {
                (DateTime calculatedStart, DateTime calculatedEnd) = CalculateDateRange(predefined.Value);
                startDate = calculatedStart;
                endDate = calculatedEnd;
            }

            List<TopOrganizerDTO> topOrganizers = await GetTopOrganizersAsync(topCount, startDate, endDate);
            List<TopMentorDTO> topMentors = await GetTopMentorsByCertificatesAsync(topCount, startDate, endDate);
            List<TopStudentDTO> topStudents = await GetTopStudentsByCertificatesAsync(topCount, startDate, endDate);

            TopPerformersDTO topPerformers = new TopPerformersDTO
            {
                TopOrganizers = topOrganizers,
                TopMentors = topMentors,
                TopStudents = topStudents
            };

            return topPerformers;
        }

        public async Task<SchoolMetricsDTO> GetSchoolMetricsAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined,
            int topSchoolCount = DEFAULT_TOP_SCHOOL_COUNT)
        {
            // Calculate date range if predefined option is specified
            if (predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom)
            {
                (DateTime calculatedStart, DateTime calculatedEnd) = CalculateDateRange(predefined.Value);
                startDate = calculatedStart;
                endDate = calculatedEnd;
            }

            IGenericRepository<School> schoolRepo = _unitOfWork.GetRepository<School>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
            IGenericRepository<Province> provinceRepo = _unitOfWork.GetRepository<Province>();

            int totalSchools = await GetTotalSchoolsAsync(schoolRepo);
            List<TopSchoolDTO> topSchools = await GetTopSchoolsByParticipationAsync(
                topSchoolCount,
                startDate,
                endDate);
            Dictionary<string, int> teamsByProvince = await GetTeamsByProvinceAsync(
                teamRepo,
                provinceRepo,
                startDate,
                endDate);

            SchoolMetricsDTO schoolMetrics = new SchoolMetricsDTO
            {
                TotalSchools = totalSchools,
                TopSchoolsByParticipation = topSchools,
                TeamsByProvince = teamsByProvince
            };

            return schoolMetrics;
        }

        /// <summary>
        /// Calculates date range based on predefined option
        /// </summary>
        /// <param name="predefined"></param>
        /// <returns></returns>
        private (DateTime StartDate, DateTime EndDate) CalculateDateRange(
            TimeRangePredefinedEnum predefined)
        {
            DateTime now = DateTime.UtcNow;
            DateTime startDate;
            DateTime endDate = now;

            switch (predefined)
            {
                case TimeRangePredefinedEnum.CurrentMonth:
                    startDate = new DateTime(now.Year, now.Month, 1);
                    break;

                case TimeRangePredefinedEnum.Last3Months:
                    startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-2);
                    break;

                case TimeRangePredefinedEnum.Last6Months:
                    startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
                    break;

                case TimeRangePredefinedEnum.LastYear:
                    startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
                    break;

                default:
                    startDate = DateTime.MinValue;
                    endDate = DateTime.MaxValue;
                    break;
            }

            return (startDate, endDate);
        }

        /// <summary>
        /// Builds base contest query with date filtering
        /// </summary>
        private static IQueryable<Contest> BuildContestQuery(
            IGenericRepository<Contest> contestRepo,
            DateTime? startDate,
            DateTime? endDate)
        {
            IQueryable<Contest> query = contestRepo.Entities
                .Where(c => c.DeletedAt == null);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                query = query.Where(c => c.CreatedAt >= startDate.Value);
            }
            

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                query = query.Where(c => c.CreatedAt <= endOfDay);
            }

            return query;
        }

        /// <summary>
        /// Gets contest status breakdown counts
        /// </summary>
        private static async Task<ContestStatusBreakdownDTO> GetStatusBreakdownAsync(
            IQueryable<Contest> contestQuery)
        {
            List<StatusCount> statusCounts = await contestQuery
                .GroupBy(c => c.Status)
                .Select(g => new StatusCount { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            ContestStatusBreakdownDTO breakdown = new ContestStatusBreakdownDTO
            {
                Published = GetCountByStatus(statusCounts, ContestStatusEnum.Published),
                RegistrationOpen = GetCountByStatus(statusCounts, ContestStatusEnum.RegistrationOpen),
                RegistrationClosed = GetCountByStatus(statusCounts, ContestStatusEnum.RegistrationClosed),
                Ongoing = GetCountByStatus(statusCounts, ContestStatusEnum.Ongoing),
                Paused = GetCountByStatus(statusCounts, ContestStatusEnum.Paused),
                Completed = GetCountByStatus(statusCounts, ContestStatusEnum.Completed),
                Delayed = GetCountByStatus(statusCounts, ContestStatusEnum.Delayed),
                Draft = GetCountByStatus(statusCounts, ContestStatusEnum.Draft),
                Cancelled = GetCountByStatus(statusCounts, ContestStatusEnum.Cancelled)
            };

            return breakdown;
        }

        /// <summary>
        /// Gets count for a specific status from status counts list
        /// </summary>
        private static int GetCountByStatus(
            List<StatusCount> statusCounts,
            ContestStatusEnum status)
        {
            return statusCounts
                .FirstOrDefault(x => x.Status == status.ToString())?.Count ?? 0;
        }

        /// <summary>
        /// Gets list of valid contest IDs (excludes Draft and Cancelled)
        /// </summary>
        private static async Task<List<Guid>> GetValidContestIdsAsync(
            IQueryable<Contest> contestQuery)
        {
            return await contestQuery
                .Where(c => ValidStatuses.Contains(c.Status))
                .Select(c => c.ContestId)
                .ToListAsync();
        }

        /// <summary>
        /// Gets total teams count (excludes eliminated/disqualified teams)
        /// </summary>
        private static async Task<int> GetTotalTeamsAsync(
            IGenericRepository<Team> teamRepo,
            List<Guid> validContestIds)
        {
            return await teamRepo.Entities
                .Where(t => t.DeletedAt == null
                            && validContestIds.Contains(t.ContestId)
                            && t.Status != TeamStatusConstants.Eliminated)
                .CountAsync();
        }

        /// <summary>
        /// Gets total students count from valid teams
        /// </summary>
        private static async Task<int> GetTotalStudentsAsync(
            IGenericRepository<Team> teamRepo,
            List<Guid> validContestIds)
        {
            return await teamRepo.Entities
                .Where(t => t.DeletedAt == null
                            && validContestIds.Contains(t.ContestId)
                            && t.Status != TeamStatusConstants.Eliminated)
                .SelectMany(t => t.TeamMembers)
                .Select(tm => tm.StudentId)
                .Distinct()
                .CountAsync();
        }

        /// <summary>
        /// Calculates growth rate
        /// </summary>
        private static async Task<(double GrowthRate, int LastMonthCount)> CalculateGrowthRateAsync(
            IGenericRepository<Contest> contestRepo)
        {
            DateTime now = DateTime.UtcNow;

            // Previous month
            DateTime previousMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
            DateTime previousMonthEnd = new DateTime(now.Year, now.Month, 1);

            // Month before previous month
            DateTime twoMonthsAgoStart = previousMonthStart.AddMonths(-1);
            DateTime twoMonthsAgoEnd = previousMonthStart;

            // Get contest counts for previous month
            int previousMonthContests = await contestRepo.Entities
                .Where(c => c.DeletedAt == null
                            && ValidStatuses.Contains(c.Status)
                            && c.CreatedAt >= previousMonthStart
                            && c.CreatedAt < previousMonthEnd)
                .CountAsync();

            // Get contest counts for two months ago
            int twoMonthsAgoContests = await contestRepo.Entities
                .Where(c => c.DeletedAt == null
                            && ValidStatuses.Contains(c.Status)
                            && c.CreatedAt >= twoMonthsAgoStart
                            && c.CreatedAt < twoMonthsAgoEnd)
                .CountAsync();

            // Calculate growth rate
            double growthRate = twoMonthsAgoContests > 0
                ? Math.Round(((previousMonthContests - twoMonthsAgoContests) / (double)twoMonthsAgoContests) * 100, 2)
                : 0;

            return (growthRate, previousMonthContests);
        }

        /// <summary>
        /// Helper class for status count grouping
        /// </summary>
        private class StatusCount
        {
            public string Status { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        /// <summary>
        /// Gets contest creation trend data 
        /// Groups by month within the date range
        /// </summary>
        private static async Task<List<TrendDataPoint>> GetContestCreationTrendAsync(
            IQueryable<Contest> contestQuery)
        {
            List<TrendDataPoint> trendData = await contestQuery
                .Where(c => ValidStatuses.Contains(c.Status))
                .GroupBy(c => new { c.CreatedAt.Year, c.CreatedAt.Month })
                .Select(g => new TrendDataPoint
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            // Format labels as "Jan 2025"
            foreach (TrendDataPoint point in trendData)
            {
                point.Label = FormatMonthLabel(point.Year, point.Month);
            }

            return trendData;
        }

        /// <summary>
        /// Ensures all months in the range are included with 0 values if no data exists
        /// </summary>
        private static List<TrendDataPoint> FillMonthGapsInTrend(
            List<TrendDataPoint> contestTrend,
            List<TrendDataPoint> teamTrend,
            DateTime startDate,
            DateTime endDate)
        {
            // Create dictionaries for quick lookup
            Dictionary<(int Year, int Month), int> contestData = contestTrend
                .ToDictionary(x => (x.Year, x.Month), x => x.Count);

            Dictionary<(int Year, int Month), int> teamData = teamTrend
                .ToDictionary(x => (x.Year, x.Month), x => x.Count);

            // Generate all months in the range
            List<TrendDataPoint> allMonths = new List<TrendDataPoint>();
            DateTime current = new DateTime(startDate.Year, startDate.Month, 1);
            DateTime end = new DateTime(endDate.Year, endDate.Month, 1);

            while (current <= end)
            {
                var key = (current.Year, current.Month);

                allMonths.Add(new TrendDataPoint
                {
                    Year = current.Year,
                    Month = current.Month,
                    Label = FormatMonthLabel(current.Year, current.Month),
                    ContestCount = contestData.GetValueOrDefault(key, 0),
                    TeamCount = teamData.GetValueOrDefault(key, 0)
                });

                current = current.AddMonths(1);
            }

            return allMonths;
        }

        /// <summary>
        /// Merges contest and team trend data to ensure all months are present
        /// Assigns 0 for months that don't have data in either trend
        /// </summary>
        private static List<TrendDataPoint> MergeTrendData(
            List<TrendDataPoint> contestTrend,
            List<TrendDataPoint> teamTrend)
        {
            // Create dictionaries for quick lookup
            Dictionary<(int Year, int Month), int> contestData = contestTrend
                .ToDictionary(x => (x.Year, x.Month), x => x.Count);

            Dictionary<(int Year, int Month), int> teamData = teamTrend
                .ToDictionary(x => (x.Year, x.Month), x => x.Count);

            // Get all unique year-month combinations
            HashSet<(int Year, int Month)> allMonths = new HashSet<(int Year, int Month)>();
            allMonths.UnionWith(contestData.Keys);
            allMonths.UnionWith(teamData.Keys);

            // Build merged list with all months
            List<TrendDataPoint> mergedTrend = allMonths
                .Select(month => new TrendDataPoint
                {
                    Year = month.Year,
                    Month = month.Month,
                    Label = FormatMonthLabel(month.Year, month.Month),
                    ContestCount = contestData.GetValueOrDefault(month, 0),
                    TeamCount = teamData.GetValueOrDefault(month, 0)
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToList();

            return mergedTrend;
        }

        /// <summary>
        /// Gets team registration trend data 
        /// Groups by month when teams were created
        /// </summary>
        private static async Task<List<TrendDataPoint>> GetTeamRegistrationTrendAsync(
            IGenericRepository<Team> teamRepo,
            DateTime? startDate,
            DateTime? endDate)
        {
            IQueryable<Team> teamQuery = teamRepo.Entities
                .Where(t => t.DeletedAt == null
                            && t.Status != TeamStatusConstants.Eliminated
                            && t.Status != TeamStatusConstants.Disqualified);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                teamQuery = teamQuery.Where(t => t.CreatedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                teamQuery = teamQuery.Where(t => t.CreatedAt < endOfDay);
            }

            List<TrendDataPoint> trendData = await teamQuery
                .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month })
                .Select(g => new TrendDataPoint
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            // Format labels
            foreach (TrendDataPoint point in trendData)
            {
                point.Label = FormatMonthLabel(point.Year, point.Month);
            }

            return trendData;
        }

        /// <summary>
        /// Gets contest status distribution
        /// Excludes Draft and Cancelled
        /// </summary>
        private static async Task<Dictionary<string, int>> GetContestStatusDistributionAsync(
            IQueryable<Contest> contestQuery)
        {
            Dictionary<string, int> distribution = await contestQuery
                .Where(c => ValidStatuses.Contains(c.Status))
                .GroupBy(c => c.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);

            return distribution;
        }

        /// <summary>
        /// Formats month label for chart display
        /// </summary>
        private static string FormatMonthLabel(int year, int month)
        {
            DateTime date = new DateTime(year, month, 1);
            return date.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets top organizers by number of completed contests
        /// Only counts contests with status Completed
        /// </summary>
        private async Task<List<TopOrganizerDTO>> GetTopOrganizersAsync(
            int topCount,
            DateTime? startDate,
            DateTime? endDate)
        {
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            IQueryable<Contest> contestQuery = contestRepo.Entities
                .Where(c => c.DeletedAt == null
                            && c.Status == ContestStatusEnum.Completed.ToString()
                            && !string.IsNullOrEmpty(c.CreatedBy));

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                contestQuery = contestQuery.Where(c => c.CreatedAt >= startDate.Value);
            } 

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                contestQuery = contestQuery.Where(c => c.CreatedAt < endOfDay);
            }

            // Group by organizer and count completed contests
            List<OrganizerStats> organizerStats = await contestQuery
                .GroupBy(c => c.CreatedBy)
                .Select(g => new OrganizerStats
                {
                    UserId = g.Key!,
                    CompletedContestsCount = g.Count(),
                    ContestIds = g.Select(c => c.ContestId).ToList()
                })
                .OrderByDescending(x => x.CompletedContestsCount)
                .Take(topCount)
                .ToListAsync();

            // Get user details and team counts
            List<Guid> userIds = organizerStats
                .Select(x => Guid.Parse(x.UserId))
                .ToList();

            Dictionary<Guid, User> users = await userRepo.Entities
                .Where(u => userIds.Contains(u.UserId) && u.DeletedAt == null)
                .ToDictionaryAsync(u => u.UserId, u => u);

            List<TopOrganizerDTO> topOrganizers = new List<TopOrganizerDTO>();

            foreach (OrganizerStats stat in organizerStats)
            {
                Guid userId = Guid.Parse(stat.UserId);

                if (!users.TryGetValue(userId, out User? user))
                    continue;

                int totalTeams = await teamRepo.Entities
                    .Where(t => stat.ContestIds.Contains(t.ContestId) && t.DeletedAt == null)
                    .CountAsync();

                topOrganizers.Add(new TopOrganizerDTO
                {
                    UserId = userId,
                    FullName = user.Fullname,
                    Email = user.Email,
                    CompletedContestsCount = stat.CompletedContestsCount,
                    TotalTeamsInContests = totalTeams
                });
            }

            return topOrganizers;
        }

        /// <summary>
        /// Gets top mentors by number of team certificates
        /// Only counts certificates with Team type
        /// </summary>
        private async Task<List<TopMentorDTO>> GetTopMentorsByCertificatesAsync(
            int topCount,
            DateTime? startDate,
            DateTime? endDate)
        {
            IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
            IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();

            IQueryable<Certificate> certificateQuery = certificateRepo.Entities
                .Where(c => c.DeletedAt == null
                            && c.TeamId != null
                            && c.CertificateType == CertificateTypeConstants.Team);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                certificateQuery = certificateQuery.Where(c => c.IssuedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                certificateQuery = certificateQuery.Where(c => c.IssuedAt < endOfDay);
            }

            // Get top mentors by certificate count
            List<MentorStats> mentorStats = await certificateQuery
                .Include(c => c.Team)
                .GroupBy(c => c.Team.MentorId)
                .Select(g => new MentorStats
                {
                    MentorId = g.Key,
                    CertificateCount = g.Count(),
                    TeamIds = g.Select(c => c.TeamId!.Value).Distinct().ToList()
                })
                .OrderByDescending(x => x.CertificateCount)
                .Take(topCount)
                .ToListAsync();

            // Get mentor details
            List<Guid> mentorIds = mentorStats.Select(x => x.MentorId).ToList();

            List<Mentor> mentors = await mentorRepo.Entities
                .Where(m => mentorIds.Contains(m.MentorId) && m.DeletedAt == null)
                .Include(m => m.User)
                .Include(m => m.School)
                .ToListAsync();

            List<TopMentorDTO> topMentors = new List<TopMentorDTO>();

            foreach (MentorStats stat in mentorStats)
            {
                Mentor? mentor = mentors.FirstOrDefault(m => m.MentorId == stat.MentorId);

                if (mentor == null)
                    continue;

                topMentors.Add(new TopMentorDTO
                {
                    MentorId = mentor.MentorId,
                    UserId = mentor.UserId,
                    FullName = mentor.User.Fullname,
                    Email = mentor.User.Email,
                    SchoolName = mentor.School.Name,
                    TeamCertificatesCount = stat.CertificateCount,
                    TeamsManaged = stat.TeamIds.Count
                });
            }

            return topMentors;
        }

        /// <summary>
        /// Gets top students by total certificates (team + student)
        /// Counts both team certificates and student certificates
        /// </summary>
        private async Task<List<TopStudentDTO>> GetTopStudentsByCertificatesAsync(
            int topCount,
            DateTime? startDate,
            DateTime? endDate)
        {
            IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

            IQueryable<Certificate> certificateQuery = certificateRepo.Entities
                .Where(c => c.DeletedAt == null);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                certificateQuery = certificateQuery.Where(c => c.IssuedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                certificateQuery = certificateQuery.Where(c => c.IssuedAt < endOfDay);
            }

            // Get individual certificates per student
            Dictionary<Guid, int> individualCerts = await certificateQuery
                .Where(c => c.StudentId != null
                            && c.CertificateType == CertificateTypeConstants.Student)
                .GroupBy(c => c.StudentId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Get team certificates - need to map teams to students
            Dictionary<Guid, int> teamCerts = await certificateQuery
                .Where(c => c.TeamId != null
                            && c.CertificateType == CertificateTypeConstants.Team)
                .Join(
                    teamMemberRepo.Entities,
                    cert => cert.TeamId,
                    tm => tm.TeamId,
                    (cert, tm) => tm.StudentId)
                .GroupBy(studentId => studentId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Combine and calculate total certificates
            HashSet<Guid> allStudentIds = new HashSet<Guid>();
            allStudentIds.UnionWith(individualCerts.Keys);
            allStudentIds.UnionWith(teamCerts.Keys);

            List<StudentStats> studentStats = allStudentIds
                .Select(studentId => new StudentStats
                {
                    StudentId = studentId,
                    IndividualCertificates = individualCerts.GetValueOrDefault(studentId, 0),
                    TeamCertificates = teamCerts.GetValueOrDefault(studentId, 0)
                })
                .OrderByDescending(x => x.TotalCertificates)
                .Take(topCount)
                .ToList();

            // Get student details
            List<Guid> studentIds = studentStats.Select(x => x.StudentId).ToList();

            List<Student> students = await studentRepo.Entities
                .Where(s => studentIds.Contains(s.StudentId) && s.DeletedAt == null)
                .Include(s => s.User)
                .Include(s => s.School)
                .ToListAsync();

            List<TopStudentDTO> topStudents = new List<TopStudentDTO>();

            foreach (StudentStats stat in studentStats)
            {
                Student? student = students.FirstOrDefault(s => s.StudentId == stat.StudentId);

                if (student == null)
                    continue;

                topStudents.Add(new TopStudentDTO
                {
                    StudentId = student.StudentId,
                    UserId = student.UserId,
                    FullName = student.User.Fullname,
                    Email = student.User.Email,
                    SchoolName = student.School.Name,
                    TeamCertificatesCount = stat.TeamCertificates,
                    IndividualCertificatesCount = stat.IndividualCertificates,
                    TotalCertificates = stat.TotalCertificates
                });
            }

            return topStudents;
        }

        /// <summary>
        /// Helper class for organizer statistics
        /// </summary>
        private class OrganizerStats
        {
            public string UserId { get; set; } = string.Empty;
            public int CompletedContestsCount { get; set; }
            public List<Guid> ContestIds { get; set; } = new();
        }

        /// <summary>
        /// Helper class for mentor statistics
        /// </summary>
        private class MentorStats
        {
            public Guid MentorId { get; set; }
            public int CertificateCount { get; set; }
            public List<Guid> TeamIds { get; set; } = new();
        }

        /// <summary>
        /// Helper class for student statistics
        /// </summary>
        private class StudentStats
        {
            public Guid StudentId { get; set; }
            public int IndividualCertificates { get; set; }
            public int TeamCertificates { get; set; }
            public int TotalCertificates => IndividualCertificates + TeamCertificates;
        }

        /// <summary>
        /// Gets total number of active schools
        /// </summary>
        private static async Task<int> GetTotalSchoolsAsync(
            IGenericRepository<School> schoolRepo)
        {
            return await schoolRepo.Entities
                .Where(s => s.DeletedAt == null)
                .CountAsync();
        }

        /// <summary>
        /// Gets top schools ranked by participation metrics
        /// Ranks by Total Certificates > Total Teams > Total Students
        /// </summary>
        private async Task<List<TopSchoolDTO>> GetTopSchoolsByParticipationAsync(
            int topCount,
            DateTime? startDate,
            DateTime? endDate)
        {
            IGenericRepository<School> schoolRepo = _unitOfWork.GetRepository<School>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
            IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
            IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

            // Build team query with date filtering
            IQueryable<Team> teamQuery = teamRepo.Entities
                .Where(t => t.DeletedAt == null && t.Status != TeamStatusConstants.Eliminated);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                teamQuery = teamQuery.Where(t => t.CreatedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                teamQuery = teamQuery.Where(t => t.CreatedAt < endOfDay);
            }

            // Get team counts per school
            Dictionary<Guid, int> teamCountsBySchool = await teamQuery
                .GroupBy(t => t.SchoolId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Get student counts per school
            Dictionary<Guid, int> studentCountsBySchool = await teamQuery
                .Join(
                    teamMemberRepo.Entities,
                    t => t.TeamId,
                    tm => tm.TeamId,
                    (t, tm) => new { t.SchoolId, tm.StudentId })
                .GroupBy(x => x.SchoolId)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.Select(x => x.StudentId).Distinct().Count());

            // Build certificate query with date filtering
            IQueryable<Certificate> certQuery = certificateRepo.Entities
                .Where(c => c.DeletedAt == null);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                certQuery = certQuery.Where(c => c.IssuedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                certQuery = certQuery.Where(c => c.IssuedAt < endOfDay);
            }

            // Get certificate counts per school (from both team and student certificates)
            Dictionary<Guid, int> certificatesBySchool = new Dictionary<Guid, int>();

            // Team certificates
            Dictionary<Guid, int> teamCerts = await certQuery
                .Where(c => c.TeamId != null && c.CertificateType == CertificateTypeConstants.Team)
                .Join(
                    teamRepo.Entities.Where(t => t.DeletedAt == null),
                    cert => cert.TeamId,
                    team => team.TeamId,
                    (cert, team) => team.SchoolId)
                .GroupBy(schoolId => schoolId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Student certificates
            Dictionary<Guid, int> studentCerts = await certQuery
                .Where(c => c.StudentId != null && c.CertificateType == CertificateTypeConstants.Student)
                .Join(
                    _unitOfWork.GetRepository<Student>().Entities.Where(s => s.DeletedAt == null),
                    cert => cert.StudentId,
                    student => student.StudentId,
                    (cert, student) => student.SchoolId)
                .GroupBy(schoolId => schoolId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Combine certificate counts
            HashSet<Guid> allSchoolIds = new HashSet<Guid>();
            allSchoolIds.UnionWith(teamCerts.Keys);
            allSchoolIds.UnionWith(studentCerts.Keys);

            foreach (Guid schoolId in allSchoolIds)
            {
                certificatesBySchool[schoolId] =
                    teamCerts.GetValueOrDefault(schoolId, 0) +
                    studentCerts.GetValueOrDefault(schoolId, 0);
            }

            // Get all school IDs that have any participation
            HashSet<Guid> participatingSchoolIds = new HashSet<Guid>();
            participatingSchoolIds.UnionWith(teamCountsBySchool.Keys);
            participatingSchoolIds.UnionWith(certificatesBySchool.Keys);

            // Get school details
            List<Guid> schoolIds = participatingSchoolIds.ToList();
            Dictionary<Guid, School> schools = await schoolRepo.Entities
                .Where(s => schoolIds.Contains(s.SchoolId) && s.DeletedAt == null)
                .Include(s => s.Province)
                .ToDictionaryAsync(s => s.SchoolId, s => s);

            // Build and rank school stats
            List<TopSchoolDTO> topSchools = participatingSchoolIds
                .Where(schoolId => schools.ContainsKey(schoolId))
                .Select(schoolId => new TopSchoolDTO
                {
                    SchoolId = schoolId,
                    SchoolName = schools[schoolId].Name,
                    ProvinceName = schools[schoolId].Province.Name,
                    TotalTeams = teamCountsBySchool.GetValueOrDefault(schoolId, 0),
                    TotalStudents = studentCountsBySchool.GetValueOrDefault(schoolId, 0),
                    TotalCertificates = certificatesBySchool.GetValueOrDefault(schoolId, 0)
                })
                .OrderByDescending(s => s.TotalCertificates)
                .ThenByDescending(s => s.TotalTeams)
                .ThenByDescending(s => s.TotalStudents)
                .Take(topCount)
                .ToList();

            return topSchools;
        }

        /// <summary>
        /// Gets team distribution by province
        /// </summary>
        private async Task<Dictionary<string, int>> GetTeamsByProvinceAsync(
            IGenericRepository<Team> teamRepo,
            IGenericRepository<Province> provinceRepo,
            DateTime? startDate,
            DateTime? endDate)
        {
            IGenericRepository<School> schoolRepo = _unitOfWork.GetRepository<School>();

            IQueryable<Team> teamQuery = teamRepo.Entities
                .Where(t => t.DeletedAt == null && t.Status != TeamStatusConstants.Eliminated);

            if (startDate.HasValue)
            {
                // Start date: beginning of the day (00:00:00)
                teamQuery = teamQuery.Where(t => t.CreatedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // End date: end of the day (23:59:59)
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                teamQuery = teamQuery.Where(t => t.CreatedAt < endOfDay);
            }

            // Get team counts per school with province info
            Dictionary<Guid, int> teamsBySchoolId = await teamQuery
                .GroupBy(t => t.SchoolId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Get school to province mapping
            List<Guid> schoolIds = teamsBySchoolId.Keys.ToList();

            Dictionary<Guid, Guid> schoolToProvince = await schoolRepo.Entities
                .Where(s => schoolIds.Contains(s.SchoolId) && s.DeletedAt == null)
                .ToDictionaryAsync(s => s.SchoolId, s => s.ProvinceId);

            // Aggregate by province
            Dictionary<Guid, int> teamsByProvinceId = teamsBySchoolId
                .Where(kvp => schoolToProvince.ContainsKey(kvp.Key))
                .GroupBy(kvp => schoolToProvince[kvp.Key])
                .ToDictionary(g => g.Key, g => g.Sum(kvp => kvp.Value));

            // Get province names
            List<Guid> provinceIds = teamsByProvinceId.Keys.ToList();
            Dictionary<Guid, string> provinceNames = await provinceRepo.Entities
                .Where(p => provinceIds.Contains(p.ProvinceId))
                .ToDictionaryAsync(p => p.ProvinceId, p => p.Name);

            // Build final result with province names
            Dictionary<string, int> result = teamsByProvinceId
                .Where(kvp => provinceNames.ContainsKey(kvp.Key))
                .ToDictionary(
                    kvp => provinceNames[kvp.Key],
                    kvp => kvp.Value);

            return result.OrderByDescending(kvp => kvp.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}
