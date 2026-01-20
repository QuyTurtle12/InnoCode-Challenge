using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Dashboards;
using BusinessLogic.IServices.NotificationsAndLogs;
using BusinessLogic.IServices.Students;
using DataAccess.Entities;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TeamDTOs;
using Repository.DTOs.TeamMemberDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Students
{
    public class TeamService : ITeamService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILeaderboardEntryService _leaderboardEntryService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IActivityLogWriter _logWriter;
        private readonly IDashboardNotifierService _dashboardNotifier;

        public TeamService(
            IUOW unitOfWork,
            IMapper mapper,
            ILeaderboardEntryService leaderboardEntryService,
            IHttpContextAccessor httpContextAccessor,
            IActivityLogWriter logWriter,
            IDashboardNotifierService dashboardNotifier)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _leaderboardEntryService = leaderboardEntryService;
            _httpContextAccessor = httpContextAccessor;
            _logWriter = logWriter;
            _dashboardNotifier = dashboardNotifier;
        }

        public async Task<PaginatedList<TeamWithMembersDTO>> GetAsync(
            int pageNumber,
            int pageSize,
            Guid? contestIdSearch,
            Guid? schoolIdSearch,
            Guid? mentorIdSearch,
            string? nameSearch,
            bool IsMyTeam = false)
        {
            // Get the repository
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Build a query
            IQueryable<Team> query = teamRepo.Entities
                .Where(t => t.DeletedAt == null)
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor)
                    .ThenInclude(m => m.User)
                .Include(t => t.TeamMembers)
                    .ThenInclude(tm => tm.Student)
                        .ThenInclude(s => s.User);

            // Get current user teams only
            if (IsMyTeam)
            {
                Guid currentUserId = Guid.Parse(GetCurrentUserIdOrThrow());

                // Filter teams where the current user is either the mentor or a team member
                query = query.Where(t =>
                    t.Mentor != null && t.Mentor.UserId == currentUserId ||
                    t.TeamMembers.Any(tm => tm.Student != null && tm.Student.UserId == currentUserId));
            }

            // Apply filters
            if (contestIdSearch.HasValue)
            {
                query = query.Where(t => t.ContestId == contestIdSearch.Value);
            }

            if (schoolIdSearch.HasValue)
            {
                query = query.Where(t => t.SchoolId == schoolIdSearch.Value);
            }

            if (mentorIdSearch.HasValue)
            {
                query = query.Where(t => t.MentorId == mentorIdSearch.Value);
            }

            if (!string.IsNullOrWhiteSpace(nameSearch))
            {
                var trimmedName = nameSearch.Trim();
                query = query.Where(t => t.Name.Contains(trimmedName));
            }

            // Order by creation date
            query = query.OrderByDescending(t => t.CreatedAt);

            // Apply pagination
            PaginatedList<Team> resultQuery = await teamRepo.GetPagingAsync(query, pageNumber, pageSize);

            // Map to DTOs using AutoMapper
            IReadOnlyCollection<TeamWithMembersDTO> teamDTOs = resultQuery.Items.Select(team =>
            {
                // Map to DTO
                TeamWithMembersDTO dto = _mapper.Map<TeamWithMembersDTO>(team);

                // Sort members by leader first, then by fullname
                dto.Members = dto.Members
                    .OrderByDescending(m => m.MemberRole == MemberRoleEnum.Leader)
                    .ThenBy(m => m.StudentFullname)
                    .ToList();

                return dto;
            }).ToList();

            return new PaginatedList<TeamWithMembersDTO>(teamDTOs, resultQuery.TotalCount, resultQuery.PageNumber, resultQuery.PageSize);
        }

        public async Task<TeamWithMembersDTO> GetByIdAsync(Guid id)
        {
            // Get the repository
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Retrieve the team with related data
            Team? team = await teamRepo.Entities
                .Where(t => t.TeamId == id && t.DeletedAt == null)
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor)
                    .ThenInclude(m => m.User)
                .Include(t => t.TeamMembers)
                    .ThenInclude(tm => tm.Student)
                        .ThenInclude(s => s.User)
                .FirstOrDefaultAsync();

            // Handle not found
            if (team == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"No team with ID={id}");
            }

            // Map to DTO using AutoMapper
            TeamWithMembersDTO dto = _mapper.Map<TeamWithMembersDTO>(team);

            // Sort members by leader first, then by fullname
            dto.Members = dto.Members
                .OrderByDescending(m => m.MemberRole == MemberRoleEnum.Leader)
                .ThenBy(m => m.StudentFullname)
                .ToList();

            return dto;
        }

        public async Task<TeamDTO> CreateAsync(CreateTeamDTO dto)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var contestRepository = _unitOfWork.GetRepository<Contest>();
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            // mentorId diff userId
            string userId = GetCurrentUserIdOrThrow();
            bool hasUserGuid = Guid.TryParse(userId, out Guid userGuid);

            var meAsMentor = await mentorRepository.Entities
                .Include(m => m.User)
                .FirstOrDefaultAsync(m =>
                    m.User != null &&
                    ((hasUserGuid && EF.Property<Guid>(m.User, "UserId") == userGuid) ||
                     m.User.UserId.ToString() == userId));
            if (meAsMentor == null)
                throw new ErrorException(StatusCodes.Status403Forbidden, "NOT_MENTOR",
                    "Only mentors can create teams.");

            bool contestExists = await contestRepository.Entities.AnyAsync(c => c.ContestId == dto.ContestId);
            if (!contestExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={dto.ContestId}");

            bool schoolExists = await schoolRepository.Entities.AnyAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
            if (!schoolExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId}");

            if (meAsMentor.SchoolId != dto.SchoolId)
                throw new ErrorException(StatusCodes.Status409Conflict, "MENTOR_NOT_BELONG_TO_SCHOOL",
                    "This mentor does not belong to the selected school.");

            var trimmedName = dto.Name.Trim();

            bool duplicateName = await teamRepository.Entities.AnyAsync(t =>
                t.ContestId == dto.ContestId &&
                t.DeletedAt == null &&
                t.Name.ToLower() == trimmedName.ToLower());
            if (duplicateName)
                throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                    "A team with this name already exists in the contest.");

            bool mentorAlreadyHasActive = await teamRepository.Entities.AnyAsync(t =>
            t.ContestId == dto.ContestId &&
            t.MentorId == meAsMentor.MentorId &&
            t.DeletedAt == null &&
            t.Status == TeamStatusConstants.Active);

            if (mentorAlreadyHasActive)
                throw new ErrorException(StatusCodes.Status409Conflict,
                    TeamErrorCodeConstants.MentorContestLimit,
                    "A mentor can have only one active team in the same contest.");

            var now = DateTime.UtcNow;
            var team = new Team
            {
                TeamId = Guid.NewGuid(),
                Name = trimmedName,
                ContestId = dto.ContestId,
                SchoolId = dto.SchoolId,
                MentorId = meAsMentor.MentorId,
                CreatedAt = now,
                DeletedAt = null,
                Status = TeamStatusConstants.Active
            };

            await teamRepository.InsertAsync(team);
            await _unitOfWork.SaveAsync();

            if (Guid.TryParse(userId, out var actorId))
            {
                await _logWriter.TryWriteAsync(
                    actorId,
                    ActivityActions.TeamCreate,
                    TargetTypes.Team,
                    team.TeamId.ToString());
            }

            await _leaderboardEntryService.AddTeamToLeaderboardAsync(dto.ContestId, team.TeamId);

            // Notify dashboard about new team registration
            await _dashboardNotifier.NotifyTeamRegisteredAsync();
            await _dashboardNotifier.NotifyMentorDashboardUpdatedAsync(team.MentorId);
            await NotifyOrganizerDashboardUpdatedAsync(dto.ContestId, contestRepository);

            var created = await teamRepository.Entities
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .AsNoTracking()
                .FirstAsync(t => t.TeamId == team.TeamId);

            return _mapper.Map<TeamDTO>(created);
        }


        public async Task<TeamDTO> UpdateAsync(Guid id, UpdateTeamDTO dto)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            var team = await teamRepository.Entities
                .Include(t => t.Contest)
                .FirstOrDefaultAsync(t => t.TeamId == id && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={id}");

            await EnsureMentorOwnsTeamOrAdminAsync(team, mentorRepository);

            EnsureContestNotStarted(team.Contest);

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                string newName = dto.Name.Trim();
                bool duplicate = await teamRepository.Entities.AnyAsync(t =>
                    t.TeamId != id &&
                    t.ContestId == team.ContestId &&
                    t.DeletedAt == null &&
                    t.Name.ToLower() == newName.ToLower());
                if (duplicate)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                        "A team with this name already exists in the contest.");

                team.Name = newName;
            }

            teamRepository.Update(team);
            await _unitOfWork.SaveAsync();

            string actorUserId = GetCurrentUserIdOrThrow();
            if (Guid.TryParse(actorUserId, out var actorId))
            {
                await _logWriter.TryWriteAsync(
                    actorId,
                    ActivityActions.TeamUpdate,
                    TargetTypes.Team,
                    team.TeamId.ToString());
            }

            var updated = await teamRepository.Entities
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .AsNoTracking()
                .FirstAsync(t => t.TeamId == id);

            // Notify mentor dashboard
            await _dashboardNotifier.NotifyMentorDashboardUpdatedAsync(team.MentorId);

            IGenericRepository<Contest> _contestRepo = _unitOfWork.GetRepository<Contest>();

            // Notify organizer dashboard
            Contest? contest = await _contestRepo.GetByIdAsync(team.ContestId);
            if (Guid.TryParse(contest?.CreatedBy, out Guid organizerId))
            {
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);
            }

            return _mapper.Map<TeamDTO>(updated);
        }

        public async Task DeleteAsync(Guid id)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            var team = await teamRepository.Entities
                .Include(t => t.TeamMembers)
                .Include(t => t.Contest)
                .Include(t => t.Submissions)
                .Include(t => t.LeaderboardEntries)
                .Include(t => t.Certificates)
                .Include(t => t.Appeals)
                .FirstOrDefaultAsync(t => t.TeamId == id && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={id}");

            await EnsureMentorOwnsTeamOrAdminAsync(team, mentorRepository);

            EnsureContestNotStarted(team.Contest);

            bool hasRelations = team.TeamMembers.Any() ||
                                team.Submissions.Any() ||
                                team.LeaderboardEntries.Any() ||
                                team.Certificates.Any() ||
                                team.Appeals.Any();

            if (hasRelations)
                throw new ErrorException(StatusCodes.Status409Conflict, "TEAM_IN_USE",
                    "Cannot delete a team that has related records.");

            team.DeletedAt = DateTime.UtcNow;
            teamRepository.Update(team);
            await _unitOfWork.SaveAsync();

            // Notify mentor dashboard
            await _dashboardNotifier.NotifyMentorDashboardUpdatedAsync(team.MentorId);

            IGenericRepository<Contest> _contestRepo = _unitOfWork.GetRepository<Contest>();

            // Notify organizer dashboard
            Contest? contest = await _contestRepo.GetByIdAsync(team.ContestId);
            if (Guid.TryParse(contest?.CreatedBy, out Guid organizerId))
            {
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);
            }
        }

        public async Task RemoveMemberAsync(Guid teamId, Guid studentId)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();
            var memberRepository = _unitOfWork.GetRepository<TeamMember>();

            var currentMentor = await GetCurrentMentorOrThrowAsync(mentorRepository);

            var team = await teamRepository.Entities
                .Include(t => t.Contest)
                .Include(t => t.TeamMembers)
                .FirstOrDefaultAsync(t => t.TeamId == teamId && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"No team with ID={teamId}");

            if (team.MentorId != currentMentor.MentorId)
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN, "You do not manage this team.");

            EnsureContestNotStarted(team.Contest);

            var member = team.TeamMembers.FirstOrDefault(tm => tm.StudentId == studentId);
            if (member == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Team member not found.");

            memberRepository.Delete(member);
            await _unitOfWork.SaveAsync();

            string actorUserId = GetCurrentUserIdOrThrow();
            if (Guid.TryParse(actorUserId, out var actorId))
            {
                await _logWriter.TryWriteAsync(
                    actorId,
                    ActivityActions.TeamMemberRemove,
                    TargetTypes.Team,
                    team.TeamId.ToString());
            }

            // Notify mentor dashboard
            await _dashboardNotifier.NotifyMentorDashboardUpdatedAsync(team.MentorId);

            IGenericRepository<Contest> _contestRepo = _unitOfWork.GetRepository<Contest>();

            // Notify organizer dashboard
            Contest? contest = await _contestRepo.GetByIdAsync(team.ContestId);
            if (Guid.TryParse(contest?.CreatedBy, out Guid organizerId))
            {
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);
            }
        }

        public async Task<IReadOnlyList<TeamWithMembersDTO>> GetMyTeamsAsync()
        {
            string userId = GetCurrentUserIdOrThrow();

            var teamRepo = _unitOfWork.GetRepository<Team>();
            var studentRepo = _unitOfWork.GetRepository<Student>();
            var mentorRepo = _unitOfWork.GetRepository<Mentor>();

            bool hasUserGuid = Guid.TryParse(userId, out Guid userGuid);

            Guid? myStudentId = await studentRepo.Entities
                .Where(s => s.DeletedAt == null
                    && ((hasUserGuid && EF.Property<Guid>(s, "UserId") == userGuid)
                        || s.UserId.ToString() == userId))
                .Select(s => (Guid?)s.StudentId)
                .FirstOrDefaultAsync();

            // mentorId diff userId
            Guid? myMentorId = await mentorRepo.Entities
                .Include(m => m.User)
                .Where(m => m.User != null
                    && ((hasUserGuid && EF.Property<Guid>(m.User, "UserId") == userGuid)
                        || m.User.UserId.ToString() == userId))
                .Select(m => (Guid?)m.MentorId)
                .FirstOrDefaultAsync();

            IQueryable<Team> q = teamRepo.Entities
                .Where(t => t.DeletedAt == null)
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .Include(t => t.TeamMembers).ThenInclude(tm => tm.Student).ThenInclude(s => s.User);

            if (myStudentId.HasValue && myMentorId.HasValue)
            {
                q = q.Where(t => t.MentorId == myMentorId.Value
                              || t.TeamMembers.Any(tm => tm.StudentId == myStudentId.Value));
            }
            else if (myStudentId.HasValue)
            {
                q = q.Where(t => t.TeamMembers.Any(tm => tm.StudentId == myStudentId.Value));
            }
            else if (myMentorId.HasValue)
            {
                q = q.Where(t => t.MentorId == myMentorId.Value);
            }
            else
            {
                return Array.Empty<TeamWithMembersDTO>();
            }

            var teams = await q.OrderByDescending(t => t.CreatedAt).ToListAsync();

            var result = teams.Select(t => new TeamWithMembersDTO
            {
                TeamId = t.TeamId,
                Name = t.Name,
                ContestId = t.ContestId,
                ContestName = t.Contest?.Name ?? "N/A",
                SchoolId = t.SchoolId,
                SchoolName = t.School?.Name ?? "N/A",
                MentorId = t.MentorId,
                MentorName = t.Mentor?.User?.Fullname ?? "N/A",
                CreatedAt = t.CreatedAt,
                Members = t.TeamMembers
                    .OrderByDescending(tm => tm.MemberRole == "Leader")
                    .ThenBy(tm => tm.Student.User.Fullname)
                    .Select(tm => new TeamMemberDTO
                    {
                        TeamId = tm.TeamId,
                        TeamName = t.Name,
                        StudentId = tm.StudentId,
                        StudentFullname = tm.Student.User.Fullname,
                        StudentEmail = tm.Student.User.Email,
                        MemberRole = tm.MemberRole!.Equals(MemberRoleEnum.Member.ToString())
                            ? MemberRoleEnum.Member
                            : MemberRoleEnum.Leader,
                        JoinedAt = tm.JoinedAt
                    }).ToList()
            }).ToList();

            return result;
        }
        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var id = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(id))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            return id;
        }

        private static void EnsureContestNotStarted(Contest contest)
        {
            if (contest == null) return;

            var now = DateTime.UtcNow;
            var status = contest.Status?.Trim();

            var startedByTime = contest.Start.HasValue && now >= contest.Start.Value;
            var startedByStatus =
                string.Equals(status, ContestStatusEnum.Ongoing.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, ContestStatusEnum.Paused.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, ContestStatusEnum.Completed.ToString(), StringComparison.OrdinalIgnoreCase);

            if (startedByTime || startedByStatus)
            {
                throw new ErrorException(StatusCodes.Status409Conflict, ResponseCodeConstants.CONFLICT,
                    "Contest has started. Team cannot be modified.");
            }
        }

        private async Task<Mentor> GetCurrentMentorOrThrowAsync(IGenericRepository<Mentor> mentorRepository)
        {
            string userId = GetCurrentUserIdOrThrow();
            bool hasUserGuid = Guid.TryParse(userId, out Guid userGuid);

            var mentor = await mentorRepository.Entities
                .Include(m => m.User)
                .FirstOrDefaultAsync(m =>
                    m.User != null &&
                    ((hasUserGuid && EF.Property<Guid>(m.User, "UserId") == userGuid) ||
                     m.User.UserId.ToString() == userId));

            if (mentor == null)
                throw new ErrorException(StatusCodes.Status403Forbidden, "NOT_MENTOR", "Only mentors can manage team members.");

            return mentor;
        }

        private async Task EnsureMentorOwnsTeamOrAdminAsync(Team team, IGenericRepository<Mentor> mentorRepository)
        {
            if (IsAdmin())
                return;

            var mentor = await GetCurrentMentorOrThrowAsync(mentorRepository);
            if (team.MentorId != mentor.MentorId)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                    "You do not manage this team.");
            }
        }

        private bool IsAdmin()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user != null && user.IsInRole(RoleConstants.Admin);
        }

        /// <summary>
        // Notify organizer dashboard about team registration
        /// </summary>
        private async Task NotifyOrganizerDashboardUpdatedAsync(
            Guid contestId,
            IGenericRepository<Contest> contestRepo)
        {
            Contest? contest = await contestRepo.GetByIdAsync(contestId);
            if (contest != null && Guid.TryParse(contest.CreatedBy, out Guid organizerId))
            {
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);
            }
        }

    }
}
