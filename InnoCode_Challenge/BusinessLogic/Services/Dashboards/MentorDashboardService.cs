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
using Utility.Helpers;

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
                    (DateTime calculatedStart, DateTime calculatedEnd) = DateTimeHelpers.CalculateDateRange(predefined.Value);
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
                        DateTime endOfDay = endDate.Value.Date.AddDays(1);
                        teamsQuery = teamsQuery.Where(t => t.Contest.End <= endOfDay);
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

                // Team Status Breakdown
                dashboard.TeamStatusBreakdown = new TeamStatusBreakdownDTO
                {
                    ActiveTeams = teams.Count(t =>
                        string.Equals(t.Status, TeamStatusConstants.Active)),
                    CompletedTeams = teams.Count(t =>
                        string.Equals(t.Contest?.Status, ContestStatusEnum.Completed.ToString())),
                    EliminatedTeams = teams.Count(t =>
                        string.Equals(t.Status, TeamStatusConstants.Eliminated)),
                    DisqualifiedTeams = teams.Count(t =>
                        string.Equals(t.Status, TeamStatusConstants.Disqualified))
                };

                // Contest Activity Breakdown
                List<Guid> contestIds = teams.Select(t => t.ContestId).Distinct().ToList();
                List<Contest> contests = await contestRepo.Entities
                    .Where(c => contestIds.Contains(c.ContestId) && c.DeletedAt == null)
                    .ToListAsync();

                dashboard.ContestActivity = new ContestActivityDTO
                {
                    OngoingContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Ongoing.ToString())),
                    CompletedContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.Completed.ToString())),
                    UpcomingContests = contests.Count(c =>
                        string.Equals(c.Status, ContestStatusEnum.RegistrationOpen.ToString()) ||
                        string.Equals(c.Status, ContestStatusEnum.RegistrationClosed.ToString()) ||
                        string.Equals(c.Status, ContestStatusEnum.Paused.ToString()))
                };

                // Recent Certificates
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

            // Get recent 5 certificates
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

            // Map to DTOs
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
