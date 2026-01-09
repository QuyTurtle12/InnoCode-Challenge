using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
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
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = null)
        {
            // Calculate date range if predefined option is specified
            if (predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom)
            {
                (DateTime calculatedStart, DateTime calculatedEnd) = CalculateDateRange(predefined.Value);
                startDate = calculatedStart;
                endDate = calculatedEnd;
            }

            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            IQueryable<Contest> contestQuery = BuildContestQuery(contestRepo, startDate, endDate);

            ContestStatusBreakdownDTO statusBreakdown = await GetStatusBreakdownAsync(contestQuery);
            List<Guid> validContestIds = await GetValidContestIdsAsync(contestQuery);
            int totalTeams = await GetTotalTeamsAsync(teamRepo, validContestIds);
            int totalStudents = await GetTotalStudentsAsync(teamRepo, validContestIds);
            (double growthRate, int lastMonthContests) = await CalculateGrowthRateAsync(contestRepo);

            int totalContests = statusBreakdown.TotalValidContests + 
                                statusBreakdown.Draft +
                                statusBreakdown.Cancelled;

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
                    startDate = now.AddMonths(-3);
                    break;

                case TimeRangePredefinedEnum.Last6Months:
                    startDate = now.AddMonths(-6);
                    break;

                case TimeRangePredefinedEnum.CurrentYear:
                    startDate = new DateTime(now.Year, 1, 1);
                    break;

                case TimeRangePredefinedEnum.AllTime:
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
                query = query.Where(c => c.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(c => c.CreatedAt <= endDate.Value);

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

            int previousMonthContests = await contestRepo.Entities
                .Where(c => c.DeletedAt == null
                            && ValidStatuses.Contains(c.Status)
                            && c.CreatedAt >= previousMonthStart
                            && c.CreatedAt < previousMonthEnd)
                .CountAsync();

            int twoMonthsAgoContests = await contestRepo.Entities
                .Where(c => c.DeletedAt == null
                            && ValidStatuses.Contains(c.Status)
                            && c.CreatedAt >= twoMonthsAgoStart
                            && c.CreatedAt < twoMonthsAgoEnd)
                .CountAsync();

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
    }
}
