using AutoMapper;
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

namespace BusinessLogic.Services.Dashboards
{
    public class MentorDashboardService : IMentorDashboardService
    {
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMapper _mapper;

        public MentorDashboardService(
            IUOW unitOfWork,
            IHttpContextAccessor httpContextAccessor,
            IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
            _mapper = mapper;
        }

        public async Task<MentorDashboardDTO> GetMentorDashboardAsync(
            Guid? mentorId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = null)
        {
            try
            {
                // Calculate date range if predefined option is specified
                if (predefined.HasValue && predefined.Value != TimeRangePredefinedEnum.Custom)
                {
                    (DateTime calculatedStart, DateTime calculatedEnd) = CalculateDateRange(predefined.Value);
                    startDate = calculatedStart;
                    endDate = calculatedEnd;
                }

                // Get mentor ID (from parameter or current user)
                Guid targetMentorId = mentorId ?? await GetCurrentMentorIdAsync();

                // Get repositories
                IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

                // Validate mentor exists
                Mentor? mentor = await mentorRepo.Entities
                    .Include(m => m.School)
                    .FirstOrDefaultAsync(m => m.MentorId == targetMentorId && m.DeletedAt == null);

                if (mentor == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Mentor not found.");
                }

                // Build base query for teams managed by this mentor
                IQueryable<Team> teamsQuery = teamRepo.Entities
                    .Where(t => t.MentorId == targetMentorId && t.DeletedAt == null);

                // Apply date filtering if provided
                if (startDate.HasValue || endDate.HasValue)
                {
                    teamsQuery = teamsQuery.Include(t => t.Contest);

                    if (startDate.HasValue)
                    {
                        teamsQuery = teamsQuery.Where(t => t.Contest.Start >= startDate.Value);
                    }

                    if (endDate.HasValue)
                    {
                        teamsQuery = teamsQuery.Where(t => t.Contest.End <= endDate.Value);
                    }
                }

                List<Team> teams = await teamsQuery
                    .Include(t => t.Contest)
                    .Include(t => t.School)
                    .ToListAsync();

                // Get all team IDs
                List<Guid> teamIds = teams.Select(t => t.TeamId).ToList();

                // Calculate metrics
                MentorDashboardDTO dashboard = new MentorDashboardDTO
                {
                    SchoolName = mentor.School?.Name ?? "N/A"
                };

                // Total Teams Managed
                dashboard.TotalTeamsManaged = teams.Count;

                // Contests Participated
                dashboard.ContestsParticipated = teams
                    .Select(t => t.ContestId)
                    .Distinct()
                    .Count();

                // Total Team Certificates (Team type only)
                IQueryable<Certificate> certQuery = certificateRepo.Entities
                    .Where(c => teamIds.Contains(c.TeamId!.Value)
                               && c.CertificateType == CertificateTypeConstants.Team
                               && c.DeletedAt == null);

                if (startDate.HasValue)
                {
                    certQuery = certQuery.Where(c => c.IssuedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    DateTime endOfDay = endDate.Value.Date.AddDays(1);
                    certQuery = certQuery.Where(c => c.IssuedAt < endOfDay);
                }

                dashboard.TotalTeamCertificates = await certQuery.CountAsync();

                // Total Students Mentored
                dashboard.TotalStudentsMentored = await teamMemberRepo.Entities
                    .Where(tm => teamIds.Contains(tm.TeamId))
                    .Select(tm => tm.StudentId)
                    .Distinct()
                    .CountAsync();

                // Best Performing Team (based on team + student certificates)
                dashboard.BestPerformingTeam = await GetBestPerformingTeamAsync(
                    teamIds,
                    certificateRepo,
                    teamRepo,
                    startDate,
                    endDate);

                // Team Status Breakdown
                dashboard.TeamStatusBreakdown = new TeamStatusBreakdownDTO
                {
                    ActiveTeams = teams.Count(t =>
                        string.Equals(t.Status, TeamStatusConstants.Active, StringComparison.OrdinalIgnoreCase)),
                    CompletedTeams = teams.Count(t =>
                        string.Equals(t.Contest?.Status, ContestStatusEnum.Completed.ToString(), StringComparison.OrdinalIgnoreCase)),
                    EliminatedTeams = teams.Count(t =>
                        string.Equals(t.Status, TeamStatusConstants.Eliminated, StringComparison.OrdinalIgnoreCase)),
                    DisqualifiedTeams = teams.Count(t =>
                        string.Equals(t.Status, TeamStatusConstants.Disqualified, StringComparison.OrdinalIgnoreCase))
                };

                // Contest Activity Breakdown
                List<Guid> contestIds = teams.Select(t => t.ContestId).Distinct().ToList();
                List<Contest> contests = await contestRepo.Entities
                    .Where(c => contestIds.Contains(c.ContestId) && c.DeletedAt == null)
                    .ToListAsync();

                dashboard.ContestActivity = new ContestActivityDTO
                {
                    OngoingContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Ongoing.ToString(), StringComparison.OrdinalIgnoreCase)),
                    CompletedContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Completed.ToString(), StringComparison.OrdinalIgnoreCase)),
                    UpcomingContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.RegistrationOpen.ToString(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Status, ContestStatusEnum.RegistrationClosed.ToString(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Status, ContestStatusEnum.Paused.ToString(), StringComparison.OrdinalIgnoreCase))
                };

                // Recent Certificates (last 5, Team type only)
                dashboard.RecentCertificates = await GetRecentCertificatesAsync(
                    teamIds,
                    certificateRepo,
                    teamRepo,
                    startDate,
                    endDate);

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
                    $"Error retrieving mentor dashboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculates date range based on predefined option
        /// </summary>
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
        /// Get current mentor ID from JWT token
        /// </summary>
        private async Task<Guid> GetCurrentMentorIdAsync()
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ErrorException(StatusCodes.Status401Unauthorized,
                    ResponseCodeConstants.UNAUTHORIZED,
                    "User not authenticated.");
            }

            IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();

            Mentor? mentor = await mentorRepo.Entities
                .FirstOrDefaultAsync(m => m.UserId.ToString() == userId && m.DeletedAt == null);

            if (mentor == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Mentor profile not found.");
            }

            return mentor.MentorId;
        }

        /// <summary>
        /// Get best performing team based on total certificates (team + student)
        /// Rank by total certificates, then team certificates, then fewest members, then TeamId
        /// </summary>
        private async Task<BestTeamDTO?> GetBestPerformingTeamAsync(
            List<Guid> teamIds,
            IGenericRepository<Certificate> certificateRepo,
            IGenericRepository<Team> teamRepo,
            DateTime? startDate,
            DateTime? endDate)
        {
            if (!teamIds.Any())
                return null;

            // Get team member counts
            IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

            Dictionary<Guid, int> teamMemberCounts = await teamMemberRepo.Entities
                .Where(tm => teamIds.Contains(tm.TeamId))
                .GroupBy(tm => tm.TeamId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Build certificate query with date filtering
            IQueryable<Certificate> certQuery = certificateRepo.Entities
                .Where(c => teamIds.Contains(c.TeamId!.Value) && c.DeletedAt == null);

            if (startDate.HasValue)
            {
                certQuery = certQuery.Where(c => c.IssuedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                certQuery = certQuery.Where(c => c.IssuedAt < endOfDay);
            }

            // Get all certificates for these teams
            var teamCertificates = await certQuery
                .GroupBy(c => c.TeamId)
                .Select(g => new
                {
                    TeamId = g.Key!.Value,
                    TeamCertCount = g.Count(c => c.CertificateType == CertificateTypeConstants.Team),
                    StudentCertCount = g.Count(c => c.CertificateType == CertificateTypeConstants.Student),
                    TotalCertCount = g.Count()
                })
                .ToListAsync();

            // Combine with team member counts and apply ranking
            var bestTeam = teamCertificates
                .Select(tc => new
                {
                    tc.TeamId,
                    tc.TeamCertCount,
                    tc.StudentCertCount,
                    tc.TotalCertCount,
                    MemberCount = teamMemberCounts.GetValueOrDefault(tc.TeamId, 0)
                })
                .OrderByDescending(x => x.TotalCertCount)
                .ThenByDescending(x => x.TeamCertCount)
                .ThenBy(x => x.MemberCount)
                .ThenBy(x => x.TeamId)
                .FirstOrDefault();

            if (bestTeam == null)
                return null;

            // Get team details
            Team? team = await teamRepo.Entities
                .Include(t => t.Contest)
                .FirstOrDefaultAsync(t => t.TeamId == bestTeam.TeamId);

            if (team == null)
                return null;

            return new BestTeamDTO
            {
                TeamId = team.TeamId,
                TeamName = team.Name,
                TotalCertificates = bestTeam.TotalCertCount,
                TeamCertificates = bestTeam.TeamCertCount,
                StudentCertificates = bestTeam.StudentCertCount,
                ContestName = team.Contest?.Name ?? "N/A"
            };
        }

        /// <summary>
        /// Get recent certificates (last 5, Team type only)
        /// </summary>
        private async Task<List<RecentCertificateDTO>> GetRecentCertificatesAsync(
            List<Guid> teamIds,
            IGenericRepository<Certificate> certificateRepo,
            IGenericRepository<Team> teamRepo,
            DateTime? startDate,
            DateTime? endDate)
        {
            if (!teamIds.Any())
                return new List<RecentCertificateDTO>();

            // Build certificate query with date filtering
            IQueryable<Certificate> certQuery = certificateRepo.Entities
                .Where(c => teamIds.Contains(c.TeamId!.Value)
                           && c.CertificateType == CertificateTypeConstants.Team
                           && c.DeletedAt == null);

            if (startDate.HasValue)
            {
                certQuery = certQuery.Where(c => c.IssuedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                DateTime endOfDay = endDate.Value.Date.AddDays(1);
                certQuery = certQuery.Where(c => c.IssuedAt < endOfDay);
            }

            var recentCerts = await certQuery
                .OrderByDescending(c => c.IssuedAt)
                .Take(5)
                .Select(c => new
                {
                    c.CertificateId,
                    c.TeamId,
                    c.CertificateType,
                    c.IssuedAt
                })
                .ToListAsync();

            if (!recentCerts.Any())
                return new List<RecentCertificateDTO>();

            // Get team and contest details
            var teamDetails = await teamRepo.Entities
                .Where(t => recentCerts.Select(rc => rc.TeamId!.Value).Contains(t.TeamId))
                .Include(t => t.Contest)
                .Select(t => new
                {
                    t.TeamId,
                    t.Name,
                    ContestName = t.Contest!.Name
                })
                .ToListAsync();

            return recentCerts.Select(rc =>
            {
                var team = teamDetails.FirstOrDefault(td => td.TeamId == rc.TeamId!.Value);
                return new RecentCertificateDTO
                {
                    CertificateId = rc.CertificateId,
                    TeamName = team?.Name ?? "Unknown",
                    CertificateType = rc.CertificateType ?? "Unknown",
                    ContestName = team?.ContestName ?? "Unknown",
                    IssuedAt = rc.IssuedAt
                };
            }).ToList();
        }
    }
}
