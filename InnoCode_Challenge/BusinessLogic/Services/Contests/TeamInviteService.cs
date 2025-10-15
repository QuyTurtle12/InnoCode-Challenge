using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.IRepositories;
using Repository.DTOs.TeamInviteDTOs;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class TeamInviteService : ITeamInviteService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        private const string Pending = "pending";
        private const string Accepted = "accepted";
        private const string Declined = "declined";
        private const string Revoked = "revoked";
        private const string Expired = "expired";

        public TeamInviteService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
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
                q = q.Where(i => i.Status == query.Status);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLower();
                q = q.Where(i => i.InviteeEmail != null && i.InviteeEmail.ToLower().Contains(k));
            }

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "expiresat" => query.Desc ? q.OrderByDescending(i => i.ExpiresAt) : q.OrderBy(i => i.ExpiresAt),
                "email" => query.Desc ? q.OrderByDescending(i => i.InviteeEmail) : q.OrderBy(i => i.InviteeEmail),
                "status" => query.Desc ? q.OrderByDescending(i => i.Status) : q.OrderBy(i => i.Status),
                _ => query.Desc ? q.OrderByDescending(i => i.CreatedAt) : q.OrderBy(i => i.CreatedAt),
            };

            var page = await _uow.GetRepository<TeamInvite>().GetPagingAsync(q, query.Page, query.PageSize);
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
                .FirstOrDefaultAsync(t => t.TeamId == teamId && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={teamId}");

            // Policy checks
            await EnsureRegistrationOpenAsync(team.ContestId, contestRepo, configRepo);
            var maxMembers = await GetMaxTeamMembersAsync(team.ContestId, configRepo); // default 4
            var currentMembers = await memberRepo.Entities.CountAsync(m => m.TeamId == teamId);
            if (currentMembers >= maxMembers)
                throw new ErrorException(StatusCodes.Status409Conflict, "TEAM_FULL", "Team member limit reached.");

            // Resolve target student/email
            Guid? studentId = null;
            string? inviteeEmail = null;

            if (dto.StudentId.HasValue)
            {
                var student = await studentRepo.Entities
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.StudentId == dto.StudentId.Value && s.DeletedAt == null);

                if (student == null)
                    throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", $"No student with ID={dto.StudentId}");

                inviteeEmail = student.User.Email;
                studentId = student.StudentId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.InviteeEmail))
                    throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_INPUT", "Provide StudentId or InviteeEmail.");

                inviteeEmail = dto.InviteeEmail.Trim().ToLowerInvariant();
                var user = await userRepo.Entities.FirstOrDefaultAsync(u => u.Email.ToLower() == inviteeEmail && u.DeletedAt == null);

                if (user != null && user.Role == RoleConstants.Student)
                {
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
            var ttlDays = dto.TtlDays ?? await GetInviteTtlDaysAsync(configRepo); // default 3
            var newExp = now.AddDays(ttlDays);

            if (pending != null)
            {
                pending.ExpiresAt = newExp;
                pending.Token = Guid.NewGuid().ToString("N");
                inviteRepo.Update(pending);
                await _uow.SaveAsync();

                // TODO: send email with pending.Token
                return await ProjectWithTokenAsync(pending.InviteId);
            }

            // Create new invite
            var invite = new TeamInvite
            {
                InviteId = Guid.NewGuid(),
                TeamId = teamId,
                StudentId = studentId,
                InviteeEmail = studentId == null ? inviteeEmail : null, // lock to student if known
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = newExp,
                Status = Pending,
                CreatedAt = now,
                InvitedByUserId = invitedByUserId
            };

            await inviteRepo.InsertAsync(invite);
            await _uow.SaveAsync();

            // TODO: send email with invite.Token
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
                .FirstOrDefaultAsync(i => i.InviteId == inviteId && i.TeamId == teamId);

            if (invite == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "INVITE_NOT_FOUND", "Invite not found.");

            if (invite.Status is not Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Only pending invites can be resent.");

            var now = DateTime.UtcNow;

            if (invite.ExpiresAt <= now)
            {
                // expire old, issue new
                invite.Status = Expired;
                repo.Update(invite);

                var ttlDays = await GetInviteTtlDaysAsync(configRepo);
                var newInvite = new TeamInvite
                {
                    InviteId = Guid.NewGuid(),
                    TeamId = invite.TeamId,
                    StudentId = invite.StudentId,
                    InviteeEmail = invite.InviteeEmail,
                    Token = Guid.NewGuid().ToString("N"),
                    ExpiresAt = now.AddDays(ttlDays),
                    Status = Pending,
                    CreatedAt = now,
                    InvitedByUserId = requesterUserId
                };
                await repo.InsertAsync(newInvite);
                await _uow.SaveAsync();

                // TODO: email newInvite.Token
                return await ProjectWithTokenAsync(newInvite.InviteId);
            }
            else
            {
                // extend + rotate token
                invite.Token = Guid.NewGuid().ToString("N");
                invite.ExpiresAt = now.AddDays(await GetInviteTtlDaysAsync(configRepo));
                repo.Update(invite);
                await _uow.SaveAsync();

                // TODO: email invite.Token
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

            if (invite.Status is not Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Only pending invites can be revoked.");

            invite.Status = Revoked;
            repo.Update(invite);
            await _uow.SaveAsync();
        }

        public async Task AcceptByTokenAsync(string token, Guid currentUserId)
        {
            var inviteRepo = _uow.GetRepository<TeamInvite>();
            var userRepo = _uow.GetRepository<User>();
            var studentRepo = _uow.GetRepository<Student>();
            var memberRepo = _uow.GetRepository<TeamMember>();
            var roundRepo = _uow.GetRepository<Round>();
            var configRepo = _uow.GetRepository<Config>();

            var invite = await inviteRepo.Entities
                .Include(i => i.Team).ThenInclude(t => t.Contest)
                .FirstOrDefaultAsync(i => i.Token == token);

            if (invite == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "INVITE_NOT_FOUND", "Invalid invite token.");

            if (invite.Status != Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Invite is not pending.");

            if (invite.ExpiresAt <= DateTime.UtcNow)
            {
                invite.Status = Expired;
                inviteRepo.Update(invite);
                await _uow.SaveAsync();
                throw new ErrorException(StatusCodes.Status410Gone, "INVITE_EXPIRED", "Invite has expired.");
            }

            var user = await userRepo.GetByIdAsync(currentUserId)
                       ?? throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (user.DeletedAt != null)
                throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (user.Role != RoleConstants.Student)
                throw new ErrorException(StatusCodes.Status403Forbidden, "NOT_STUDENT", "Only students can accept invites.");

            if (invite.StudentId == null)
            {
                if (!string.Equals(user.Email, invite.InviteeEmail, StringComparison.OrdinalIgnoreCase))
                    throw new ErrorException(StatusCodes.Status403Forbidden, "EMAIL_MISMATCH", "This invite does not belong to your email.");
            }

            var student = await studentRepo.Entities.FirstOrDefaultAsync(s => s.UserId == user.UserId && s.DeletedAt == null);
            if (student == null)
                throw new ErrorException(StatusCodes.Status409Conflict, "STUDENT_PROFILE_REQUIRED",
                    "Please create/complete student profile before accepting the invite.");

            if (invite.StudentId == null) invite.StudentId = student.StudentId;

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

            // add membership
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
            // TODO: notify mentor & members
        }

        public async Task DeclineByTokenAsync(string token, Guid currentUserId)
        {
            var inviteRepo = _uow.GetRepository<TeamInvite>();
            var userRepo = _uow.GetRepository<User>();

            var invite = await inviteRepo.Entities.FirstOrDefaultAsync(i => i.Token == token);
            if (invite == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "INVITE_NOT_FOUND", "Invalid invite token.");

            if (invite.Status != Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVITE_NOT_PENDING", "Invite is not pending.");

            var user = await userRepo.GetByIdAsync(currentUserId)
                       ?? throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (invite.StudentId == null)
            {
                if (!string.Equals(user.Email, invite.InviteeEmail, StringComparison.OrdinalIgnoreCase))
                    throw new ErrorException(StatusCodes.Status403Forbidden, "EMAIL_MISMATCH", "This invite does not belong to your email.");
            }

            invite.Status = Declined;
            inviteRepo.Update(invite);
            await _uow.SaveAsync();
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
            var targetRanges = await roundRepo.Entities
                .Where(r => r.ContestId == targetContestId)
                .Select(r => new { r.Start, r.End })
                .ToListAsync();

            if (!targetRanges.Any()) return false;

            var otherRanges = await memberRepo.Entities
                .Where(m => m.StudentId == studentId)
                .Include(m => m.Team).ThenInclude(t => t.Contest)
                .SelectMany(m => m.Team.Contest.Rounds.Select(r => new { r.Start, r.End }))
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

            var now = DateTime.UtcNow;
            var startOk = true;
            var endOk = true;

            var startStr = cfg.FirstOrDefault(c => c.Key == startKey)?.Value;
            var endStr = cfg.FirstOrDefault(c => c.Key == endKey)?.Value;

            if (DateTime.TryParse(startStr, out var start) && now < start) startOk = false;
            if (DateTime.TryParse(endStr, out var end) && now > end) endOk = false;

            if (!startOk || !endOk)
                throw new ErrorException(StatusCodes.Status409Conflict, "REG_CLOSED", "Registration window is closed.");
        }

        private static async Task<int> GetMaxTeamMembersAsync(Guid contestId, IGenericRepository<Config> configRepo)
        {
            var key = $"contest:{contestId}:team_members_max";
            var value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return int.TryParse(value, out var n) && n > 0 ? n : 4; // default 4
        }

        private static async Task<int> GetInviteTtlDaysAsync(IGenericRepository<Config> configRepo)
        {
            var key = "team_invite_ttl_days";
            var value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return int.TryParse(value, out var n) && n >= 1 ? n : 3; // default 3 days
        }

        private async Task<TeamInviteDTO> ProjectAsync(Guid inviteId)
        {
            var entity = await _uow.GetRepository<TeamInvite>().Entities
                .Where(i => i.InviteId == inviteId)
                .Include(i => i.Team).ThenInclude(t => t.Contest)
                .AsNoTracking()
                .FirstAsync();

            return _mapper.Map<TeamInviteDTO>(entity);
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
    }
}
