using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.TeamInviteDTOs;
using Repository.IRepositories;
using System;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class TeamInviteService : ITeamInviteService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;
        private readonly INotificationService _notificationService;
        private readonly ILogger<TeamInviteService> _logger;
        private readonly IActivityLogWriter _logWriter;

        private const string Pending = TeamInviteStatusConstants.Pending;
        private const string Accepted = TeamInviteStatusConstants.Accepted;
        private const string Declined = TeamInviteStatusConstants.Declined;
        private const string Revoked = TeamInviteStatusConstants.Revoked;
        private const string Expired = TeamInviteStatusConstants.Expired;

        public TeamInviteService(
            IUOW uow,
            IMapper mapper,
            INotificationService notificationService,
            ILogger<TeamInviteService> logger,
            IActivityLogWriter logWriter)
        {
            _uow = uow;
            _mapper = mapper;
            _notificationService = notificationService;
            _logger = logger;
            _logWriter = logWriter;
        }


        public async Task<PaginatedList<TeamInviteDTO>> GetForTeamAsync(
            Guid teamId,
            TeamInviteQueryParams query,
            Guid requesterUserId,
            string requesterRole)
        {
            await EnsureTeamAccessAsync(teamId, requesterUserId, requesterRole);

            var repo = _uow.GetRepository<TeamInvite>();
            var q = repo.Entities
                .Where(i => i.TeamId == teamId)
                .Include(i => i.Team).ThenInclude(t => t.Contest)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var status = query.Status.Trim().ToLowerInvariant();
                q = q.Where(i => i.Status != null && i.Status.ToLower() == status);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLowerInvariant();
                q = q.Where(i => i.InviteeEmail != null && i.InviteeEmail.ToLower().Contains(k));
            }

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "expiresat" => query.Desc ? q.OrderByDescending(i => i.ExpiresAt) : q.OrderBy(i => i.ExpiresAt),
                "email" => query.Desc ? q.OrderByDescending(i => i.InviteeEmail) : q.OrderBy(i => i.InviteeEmail),
                "status" => query.Desc ? q.OrderByDescending(i => i.Status) : q.OrderBy(i => i.Status),
                _ => query.Desc ? q.OrderByDescending(i => i.CreatedAt) : q.OrderBy(i => i.CreatedAt),
            };

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);
            var items = page.Items.Select(_mapper.Map<TeamInviteDTO>).ToList();
            return new PaginatedList<TeamInviteDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<TeamInviteCreatedDTO> CreateAsync(
            Guid teamId,
            CreateTeamInviteDTO dto,
            Guid invitedByUserId,
            string invitedByRole)
        {
            await EnsureTeamAccessAsync(teamId, invitedByUserId, invitedByRole);

            var teamRepo = _uow.GetRepository<Team>();
            var memberRepo = _uow.GetRepository<TeamMember>();
            var studentRepo = _uow.GetRepository<Student>();
            var userRepo = _uow.GetRepository<User>();
            var inviteRepo = _uow.GetRepository<TeamInvite>();
            var contestRepo = _uow.GetRepository<Contest>();
            var roundRepo = _uow.GetRepository<Round>();
            var configRepo = _uow.GetRepository<Config>();

            var team = await teamRepo.Entities
                .Include(t => t.Contest)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TeamId == teamId && t.DeletedAt == null)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={teamId}");

            // Policy checks
            await EnsureRegistrationOpenAsync(team.ContestId, _uow.GetRepository<Contest>(), configRepo);
            var maxMembers = await GetMaxTeamMembersAsync(team.ContestId, configRepo);
            var currentMembers = await memberRepo.Entities.CountAsync(m => m.TeamId == teamId);
            if (currentMembers >= maxMembers)
                throw new ErrorException(StatusCodes.Status409Conflict, "TEAM_FULL", "Team member limit reached.");

            // Resolve target student/email
            Guid? studentId = null;
            Guid? inviteeUserId = null;
            string? inviteeEmail = null;

            if (dto.StudentId.HasValue)
            {
                var student = await studentRepo.Entities
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.StudentId == dto.StudentId.Value && s.DeletedAt == null);

                if (student == null)
                    throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", $"No student with ID={dto.StudentId}");

                inviteeEmail = NormalizeEmail(student.User.Email);
                studentId = student.StudentId;
                inviteeUserId = student.UserId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.InviteeEmail))
                    throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_INPUT", "Provide StudentId or InviteeEmail.");

                inviteeEmail = NormalizeEmail(dto.InviteeEmail);
                var user = await userRepo.Entities.FirstOrDefaultAsync(u => u.Email.ToLower() == inviteeEmail && u.DeletedAt == null);

                if (user != null && user.Role == RoleConstants.Student)
                {
                    inviteeUserId = user.UserId;
                    var student = await studentRepo.Entities.FirstOrDefaultAsync(s => s.UserId == user.UserId && s.DeletedAt == null);
                    if (student != null) studentId = student.StudentId;
                }
            }

            // Already on a team in this contest?
            if (studentId.HasValue)
            {
                bool alreadyOnTeam = await memberRepo.Entities
                    .Include(m => m.Team)
                    .AnyAsync(m => m.StudentId == studentId.Value
                                   && m.Team.ContestId == team.ContestId
                                   && m.Team.DeletedAt == null);
                if (alreadyOnTeam)
                    throw new ErrorException(StatusCodes.Status409Conflict, "ALREADY_ON_TEAM",
                        "Student is already on a team for this contest.");
            }

            // Time conflict (round overlaps with other contests the student joined)
            if (studentId.HasValue)
            {
                bool hasConflict = await HasContestTimeConflictAsync(
                    studentId.Value, team.ContestId, roundRepo, memberRepo);
                if (hasConflict)
                    throw new ErrorException(StatusCodes.Status409Conflict, "TIME_CONFLICT",
                        "Student has a time conflict with another contest.");
            }

            // If pending invite exists for same recipient -> refresh token/expiry and reuse
            var pending = await inviteRepo.Entities
                .FirstOrDefaultAsync(i => i.TeamId == teamId
                                          && i.Status == Pending
                                          && (studentId != null
                                                ? i.StudentId == studentId
                                                : i.InviteeEmail != null && i.InviteeEmail.ToLower() == inviteeEmail));

            var now = DateTime.UtcNow;
            var ttlDays = dto.TtlDays ?? await GetInviteTtlDaysAsync(configRepo, team.ContestId);

            if (pending != null)
            {
                RefreshTokenAndExpiry(pending, now, ttlDays);
                inviteRepo.Update(pending);
                await _uow.SaveAsync();
                await NotifyInviteResentAsync(pending, team, invitedByUserId);


                return await ProjectWithTokenAsync(pending.InviteId);
            }

            var invite = CreatePendingInvite(teamId, studentId, inviteeEmail, now, ttlDays, invitedByUserId);

            await inviteRepo.InsertAsync(invite);
            await _uow.SaveAsync();
            await TryNotifyInviteeAsync(inviteeUserId, team, invite);

            await _logWriter.TryWriteAsync(invitedByUserId,
                ActivityActions.TeamInviteCreated,
                TargetTypes.TeamInvite,
                invite.InviteId.ToString());


            return await ProjectWithTokenAsync(invite.InviteId);
        }

        public async Task<TeamInviteCreatedDTO> ResendAsync(
            Guid teamId,
            Guid inviteId,
            Guid requesterUserId,
            string requesterRole)
        {
            await EnsureTeamAccessAsync(teamId, requesterUserId, requesterRole);

            var repo = _uow.GetRepository<TeamInvite>();
            var configRepo = _uow.GetRepository<Config>();

            var invite = await repo.Entities.Include(i => i.Team).ThenInclude(t => t.Contest)
                .FirstOrDefaultAsync(i => i.InviteId == inviteId && i.TeamId == teamId)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "INVITE_NOT_FOUND", "Invite not found.");

            if (!IsStatus(invite.Status, Pending))
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Only pending invites can be resent.");

            var now = DateTime.UtcNow;
            var ttlDays = await GetInviteTtlDaysAsync(configRepo, invite.Team.ContestId);

            if (invite.ExpiresAt <= now)
            {
                // expire old, issue new
                invite.Status = Expired;
                repo.Update(invite);

                var newInvite = CreatePendingInvite(invite.TeamId, invite.StudentId, invite.InviteeEmail, now, ttlDays, requesterUserId);
                await repo.InsertAsync(newInvite);
                await _uow.SaveAsync();
                await NotifyInviteResentAsync(newInvite, invite.Team, requesterUserId);

                return await ProjectWithTokenAsync(newInvite.InviteId);
            }
            else
            {
                RefreshTokenAndExpiry(invite, now, ttlDays);
                repo.Update(invite);
                await _uow.SaveAsync();
                await NotifyInviteResentAsync(invite, invite.Team, requesterUserId);

                return await ProjectWithTokenAsync(invite.InviteId);
            }
        }

        public async Task RevokeAsync(
            Guid teamId,
            Guid inviteId,
            Guid requesterUserId,
            string requesterRole)
        {
            await EnsureTeamAccessAsync(teamId, requesterUserId, requesterRole);

            var repo = _uow.GetRepository<TeamInvite>();
            var invite = await repo.Entities.FirstOrDefaultAsync(i => i.InviteId == inviteId && i.TeamId == teamId);
            if (invite == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "INVITE_NOT_FOUND", "Invite not found.");

            if (!IsStatus(invite.Status, Pending))
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Only pending invites can be revoked.");

            invite.Status = Revoked;
            repo.Update(invite);
            await _uow.SaveAsync();
            await _logWriter.TryWriteAsync(requesterUserId,
                ActivityActions.TeamInviteRevoked,
                TargetTypes.TeamInvite,
                invite.InviteId.ToString());

        }

        public async Task AcceptByTokenAsync(string token, string email)
        {
            var inviteRepo = _uow.GetRepository<TeamInvite>();
            var userRepo = _uow.GetRepository<User>();
            var studentRepo = _uow.GetRepository<Student>();
            var memberRepo = _uow.GetRepository<TeamMember>();
            var roundRepo = _uow.GetRepository<Round>();
            var configRepo = _uow.GetRepository<Config>();

            var (invite, user, _) = await ValidateInviteTokenAsync(inviteRepo, userRepo, token, email);

            var student = await studentRepo.Entities
                .FirstOrDefaultAsync(s => s.UserId == user.UserId && s.DeletedAt == null);

            if (student == null)
                throw new ErrorException(StatusCodes.Status409Conflict, "STUDENT_PROFILE_REQUIRED",
                    "Please create/complete student profile before accepting the invite.");

            if (invite.StudentId.HasValue && invite.StudentId.Value != student.StudentId)
                throw new ErrorException(StatusCodes.Status403Forbidden, "INVITE_NOT_FOR_YOU", "This invite is for a different student.");

            if (!invite.StudentId.HasValue) invite.StudentId = student.StudentId;

            // policy checks again
            var maxMembers = await GetMaxTeamMembersAsync(invite.Team.ContestId, configRepo);
            var currentCount = await memberRepo.Entities.CountAsync(m => m.TeamId == invite.TeamId);
            if (currentCount >= maxMembers)
                throw new ErrorException(StatusCodes.Status409Conflict, "TEAM_FULL", "Team member limit reached.");

            bool alreadyOnTeam = await memberRepo.Entities
                .Include(m => m.Team)
                .AnyAsync(m => m.StudentId == student.StudentId
                               && m.Team.ContestId == invite.Team.ContestId
                               && m.Team.DeletedAt == null);
            if (alreadyOnTeam)
                throw new ErrorException(StatusCodes.Status409Conflict, "ALREADY_ON_TEAM",
                    "You are already on a team in this contest.");

            bool hasConflict = await HasContestTimeConflictAsync(student.StudentId, invite.Team.ContestId, roundRepo, memberRepo);
            if (hasConflict)
                throw new ErrorException(StatusCodes.Status409Conflict, "TIME_CONFLICT",
                    "You have a time conflict with another contest.");

            // add membership + accept invite
            var membership = new TeamMember
            {
                TeamId = invite.TeamId,
                StudentId = student.StudentId,
                MemberRole = "member",
                JoinedAt = DateTime.UtcNow
            };
            await memberRepo.InsertAsync(membership);

            invite.Status = Accepted;
            inviteRepo.Update(invite);

            await _uow.SaveAsync();
            await _logWriter.TryWriteAsync(user.UserId,
                ActivityActions.TeamInviteAccepted,
                TargetTypes.TeamInvite,
                invite.InviteId.ToString());
            await _logWriter.TryWriteAsync(user.UserId,
                ActivityActions.TeamMemberAdd,
                TargetTypes.Team,
                invite.TeamId.ToString());

            // notify inviter
            var inviterUserId = invite.InvitedByUserId;

            try
            {
                await _notificationService.CreateInAppToUserAsync(inviterUserId, NotificationTypes.TeamInvitationAccepted, new
                {
                    inviteId = invite.InviteId,
                    teamId = invite.TeamId,
                    teamName = invite.Team?.Name,
                    contestId = invite.Team?.ContestId,
                    contestName = invite.Team?.Contest?.Name,
                    studentId = student.StudentId,
                    studentName = user.Fullname,
                    studentEmail = user.Email,
                    targetType = TargetTypes.TeamInvite,
                    targetId = invite.InviteId.ToString(),
                    message = $"{user.Fullname} accepted your team invitation."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send invite accepted notification. InviteId={InviteId}, InviterUserId={InviterUserId}",
                    invite.InviteId, invite.InvitedByUserId);
            }

            

        }


        public async Task DeclineByTokenAsync(string token, string email)
        {
            var inviteRepo = _uow.GetRepository<TeamInvite>();
            var userRepo = _uow.GetRepository<User>();

            var (invite, user, normEmail) = await ValidateInviteTokenAsync(inviteRepo, userRepo, token, email);

            invite.Status = Declined; // (cancelled)
            inviteRepo.Update(invite);
            await _uow.SaveAsync();
            await _logWriter.TryWriteAsync(user.UserId,
                ActivityActions.TeamInviteDeclined,
                TargetTypes.TeamInvite,
                invite.InviteId.ToString());

            //notify inviter
            try
            {
                await _notificationService.CreateInAppToUserAsync(invite.InvitedByUserId, NotificationTypes.TeamInvitationDenied, new
                {
                    inviteId = invite.InviteId,
                    teamId = invite.TeamId,
                    teamName = invite.Team?.Name,
                    contestId = invite.Team?.ContestId,
                    contestName = invite.Team?.Contest?.Name,
                    inviteeEmail = normEmail,
                    targetType = TargetTypes.TeamInvite,
                    targetId = invite.InviteId.ToString(),
                    message = $"{user.Fullname} declined your team invitation."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send invite denied notification. InviteId={InviteId}, InviterUserId={InviterUserId}",
                    invite.InviteId, invite.InvitedByUserId);
            }


        }

        private async Task<(TeamInvite Invite, User User, string NormalizedEmail)> ValidateInviteTokenAsync(
            IGenericRepository<TeamInvite> inviteRepo,
            IGenericRepository<User> userRepo,
            string token,
            string email)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_INPUT", "Token and Email are required.");

            var normEmail = NormalizeEmail(email);

            var invite = await inviteRepo.Entities
                .Include(i => i.Team).ThenInclude(t => t.Contest)
                .FirstOrDefaultAsync(i => i.Token == token);
            if (invite == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "INVITE_NOT_FOUND", "Invalid invite token.");

            if (!IsStatus(invite.Status, Pending))
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Invite is not pending.");

            if (invite.ExpiresAt <= DateTime.UtcNow)
            {
                invite.Status = Expired;
                inviteRepo.Update(invite);
                await _uow.SaveAsync();
                throw new ErrorException(StatusCodes.Status410Gone, "INVITE_EXPIRED", "Invite has expired.");
            }

            if (!string.IsNullOrWhiteSpace(invite.InviteeEmail)
                && !string.Equals(NormalizeEmail(invite.InviteeEmail), normEmail, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status403Forbidden, "EMAIL_MISMATCH", "This invite does not belong to your email.");

            var user = await userRepo.Entities
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normEmail && u.DeletedAt == null);

            if (user == null)
                throw new ErrorException(StatusCodes.Status409Conflict, "ACCOUNT_REQUIRED", "Please register with the invited email first.");

            if (user.Role != RoleConstants.Student)
                throw new ErrorException(StatusCodes.Status403Forbidden, "NOT_STUDENT", "Only students can accept team invites.");

            return (invite, user, normEmail);
        }

        // ---------- helpers ----------

        private async Task EnsureTeamAccessAsync(Guid teamId, Guid userId, string role)
        {
            var teamRepo = _uow.GetRepository<Team>();
            var mentorRepo = _uow.GetRepository<Mentor>();

            if (role == RoleConstants.Admin || role == RoleConstants.Staff) return;

            var team = await teamRepo.GetByIdAsync(teamId);
            if (team == null || team.DeletedAt != null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={teamId}");

            // mentor must own this team
            var mentor = await mentorRepo.Entities
                .FirstOrDefaultAsync(m => m.MentorId == team.MentorId && m.DeletedAt == null);

            if (mentor == null || mentor.UserId != userId)
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "You are not allowed to manage this team.");
        }

        private static async Task<bool> HasContestTimeConflictAsync(
            Guid studentId,
            Guid targetContestId,
            IGenericRepository<Round> roundRepo,
            IGenericRepository<TeamMember> memberRepo)
        {
            // get all rounds of target contest
            var targetRanges = await roundRepo.Entities
                .Where(r => r.ContestId == targetContestId && r.DeletedAt == null)
                .Select(r => new { r.Start, r.End })
                .ToListAsync();

            // no rounds -> no conflict
            if (!targetRanges.Any()) return false;

            // get all other contest rounds of teams the student has joined
            var otherRanges = await memberRepo.Entities
                .Where(m => m.StudentId == studentId)
                .Include(m => m.Team).ThenInclude(t => t.Contest)
                .Where(m => m.Team.DeletedAt == null
                            && m.Team.Status != TeamStatusConstants.Disqualified
                            && m.Team.ContestId != targetContestId)
                .SelectMany(m => m.Team.Contest.Rounds
                    .Where(r => r.DeletedAt == null)
                    .Select(r => new { r.Start, r.End }))
                .ToListAsync();

            foreach (var t in targetRanges)
                foreach (var o in otherRanges)
                    if (t.Start <= o.End && o.Start <= t.End)
                        return true;

            return false;
        }

        private static async Task EnsureRegistrationOpenAsync(
            Guid contestId,
            IGenericRepository<Contest> contestRepo,
            IGenericRepository<Config> configRepo)
        {
            var startKey = $"contest:{contestId}:registration_start";
            var endKey = $"contest:{contestId}:registration_end";

            var cfg = await configRepo.Entities
                .Where(c => (c.Key == startKey || c.Key == endKey) && c.DeletedAt == null)
                .ToListAsync();

            // Now to UTC +7
            //var now = DateTime.UtcNow.AddHours(7);
            var now = DateTime.UtcNow;

            var startOk = true;
            var endOk = true;

            var nowStr = now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

            var startStr = cfg.FirstOrDefault(c => c.Key == startKey)?.Value;
            var endStr = cfg.FirstOrDefault(c => c.Key == endKey)?.Value;

            if (TryParseUtc(startStr, out var start) && now < start) startOk = false;
            if (TryParseUtc(endStr, out var end) && now > end) endOk = false;

            if (!startOk || !endOk)
                throw new ErrorException(StatusCodes.Status409Conflict, "REG_CLOSED", "Registration window is closed.");
        }

        private static async Task<int> GetMaxTeamMembersAsync(Guid contestId, IGenericRepository<Config> configRepo)
        {
            var perContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMax(contestId) && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(perContest, out var n1) && n1 > 0) return n1;

            var global = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMax && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(global, out var n2) && n2 > 0) return n2;

            return 4;
        }

        private static bool TryParseUtc(string? str, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(str)) return false;
            if (DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                result = dt;
                return true;
            }
            return false;
        }

        private static void RefreshTokenAndExpiry(TeamInvite invite, DateTime now, int ttlDays)
        {
            invite.Token = Guid.NewGuid().ToString("N");
            invite.ExpiresAt = now.AddDays(ttlDays);
        }

        private static TeamInvite CreatePendingInvite(
            Guid teamId,
            Guid? studentId,
            string? inviteeEmail,
            DateTime now,
            int ttlDays,
            Guid invitedByUserId)
        {
            var invite = new TeamInvite
            {
                InviteId = Guid.NewGuid(),
                TeamId = teamId,
                StudentId = studentId,
                InviteeEmail = inviteeEmail,
                Status = Pending,
                CreatedAt = now,
                InvitedByUserId = invitedByUserId
            };

            RefreshTokenAndExpiry(invite, now, ttlDays);
            return invite;
        }

        private static string NormalizeEmail(string email)
            => email.Trim().ToLowerInvariant();

        private static bool IsStatus(string? current, string expected)
            => string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);


        private static async Task<int> GetInviteTtlDaysAsync(IGenericRepository<Config> configRepo, Guid contestId)
        {
            // 1) per-contest override
            var perContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestInviteTtlDays(contestId) && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(perContest, out var n1) && n1 >= 1) return n1;

            // 2) global default
            var global = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamInviteTtlDays && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(global, out var n2) && n2 >= 1) return n2;

            // 3) hard fallback
            return 3;
        }

        private async Task<TeamInviteCreatedDTO> ProjectWithTokenAsync(Guid inviteId)
        {
            var entity = await _uow.GetRepository<TeamInvite>().Entities
                .Where(i => i.InviteId == inviteId)
                .Include(i => i.Team).ThenInclude(t => t.Contest)
                .AsNoTracking()
                .FirstAsync();

            return _mapper.Map<TeamInviteCreatedDTO>(entity);
        }

        private async Task TryNotifyInviteeAsync(Guid? inviteeUserId, Team team, TeamInvite invite)
        {
            if (!inviteeUserId.HasValue) return;

            try
            {
                var email = invite.InviteeEmail is null ? null : NormalizeEmail(invite.InviteeEmail);

                await _notificationService.CreateInAppToUserAsync(inviteeUserId.Value, NotificationTypes.TeamInvitation, new
                {
                    inviteId = invite.InviteId,
                    token = invite.Token,
                    status = invite.Status,
                    expiresAt = invite.ExpiresAt,
                    inviteeEmail = email,

                    contestId = team.ContestId,
                    contestName = team.Contest?.Name,
                    teamId = team.TeamId,
                    teamName = team.Name,

                    targetType = TargetTypes.TeamInvite,
                    targetId = invite.InviteId.ToString(),
                    actions = new
                    {
                        accept = new { method = "POST", url = "/api/team-invites/accept", query = new { token = invite.Token, email } },
                        decline = new { method = "POST", url = "/api/team-invites/decline", query = new { token = invite.Token, email } }
                    },

                    message = $"You have been invited to join team '{team.Name}'."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send team invitation notification. InviteId={InviteId}, TeamId={TeamId}, InviteeUserId={InviteeUserId}",
                    invite.InviteId, team.TeamId, inviteeUserId.Value);
            }


        }

        private async Task NotifyInviteResentAsync(TeamInvite invite, Team team, Guid requesterUserId)
        {
            var inviteeUserId = await ResolveInviteeUserIdAsync(invite);
            await TryNotifyInviteeAsync(inviteeUserId, team, invite);
            await _logWriter.TryWriteAsync(requesterUserId,
                ActivityActions.TeamInviteResent,
                TargetTypes.TeamInvite,
                invite.InviteId.ToString());
        }

        private async Task<Guid?> ResolveInviteeUserIdAsync(TeamInvite invite)
        {
            var studentRepo = _uow.GetRepository<Student>();
            var userRepo = _uow.GetRepository<User>();

            if (invite.StudentId.HasValue)
            {
                var uid = await studentRepo.Entities
                    .Where(s => s.StudentId == invite.StudentId.Value && s.DeletedAt == null)
                    .Select(s => s.UserId)
                    .FirstOrDefaultAsync();

                return uid == Guid.Empty ? null : uid;
            }

            if (!string.IsNullOrWhiteSpace(invite.InviteeEmail))
            {
                var email = NormalizeEmail(invite.InviteeEmail);
                var uid = await userRepo.Entities
                    .Where(u => u.Email.ToLower() == email && u.DeletedAt == null && u.Role == RoleConstants.Student)
                    .Select(u => u.UserId)
                    .FirstOrDefaultAsync();

                return uid == Guid.Empty ? null : uid;
            }

            return null;
        }

    }
}
