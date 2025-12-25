using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.JudgeInviteDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class JudgeInviteService : IJudgeInviteService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly INotificationService _notificationService;
        private readonly ILogger<JudgeInviteService> _logger;
        private readonly IActivityLogWriter _logWriter;

        private const string Pending = JudgeInviteStatusConstants.Pending;
        private const string Accepted = JudgeInviteStatusConstants.Accepted;
        private const string Declined = JudgeInviteStatusConstants.Declined;
        private const string Revoked = JudgeInviteStatusConstants.Revoked;
        private const string Expired = JudgeInviteStatusConstants.Expired;

        public JudgeInviteService(
            IUOW uow,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessor,
            INotificationService notificationService,
            ILogger<JudgeInviteService> logger,
            IActivityLogWriter logWriter)
        {
            _uow = uow;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
            _notificationService = notificationService;
            _logger = logger;
            _logWriter = logWriter;
        }


        public async Task<PaginatedList<JudgeInviteDTO>> GetForContestAsync(
            Guid contestId,
            int page,
            int pageSize,
            JudgeInviteStatusEnum? status,
            Guid? requesterUserId,
            string? judgeNameSearch,
            string? judgeEmailSearch,
            DateTime? createdAtStart,
            DateTime? createdAtEnd,
            DateTime? ExpiresAtStart,
            DateTime? ExpiresAtEnd,
            DateTime? AcceptedAtStart,
            DateTime? AcceptedAtEnd,
            bool desc)
        {
            var contestRepo = _uow.GetRepository<Contest>();
            await EnsureContestOwnedByOrganizerAsync(contestId, contestRepo);

            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Get judge invite data
            IGenericRepository<JudgeInvite> judgeInviteRepo = _uow.GetRepository<JudgeInvite>();
            IQueryable<JudgeInvite> query = judgeInviteRepo.Entities
                .Where(i => i.ContestId == contestId)
                .Include(i => i.Contest)
                .Include(i => i.Judge)
                .AsNoTracking();

            // Filter by status using enum
            if (status.HasValue)
            {
                string statusString = status.Value.ToString().ToLowerInvariant();
                query = query.Where(i => i.Status == statusString);
            }

            // Filter by requester user ID
            if (requesterUserId.HasValue)
            {
                string requesterUserIdStr = requesterUserId.Value.ToString();
                query = query.Where(i => i.CreatedBy == requesterUserIdStr);
            }

            // Filter by judge name
            if (!string.IsNullOrWhiteSpace(judgeNameSearch))
            {
                judgeNameSearch = judgeNameSearch.Trim().ToLowerInvariant();
                query = query.Where(i => i.Judge.Fullname.ToLower().Contains(judgeNameSearch));
            }

            // Filter by judge email
            if (!string.IsNullOrWhiteSpace(judgeEmailSearch))
            {
                judgeEmailSearch = judgeEmailSearch.Trim().ToLowerInvariant();
                query = query.Where(i => i.Judge.Email.ToLower().Contains(judgeEmailSearch));
            }

            // Filter by CreatedAt date range
            if (createdAtStart.HasValue)
                query = query.Where(i => i.CreatedAt >= createdAtStart.Value);

            if (createdAtEnd.HasValue)
                query = query.Where(i => i.CreatedAt <= createdAtEnd.Value);

            // Filter by ExpiresAt date range
            if (ExpiresAtStart.HasValue)
                query = query.Where(i => i.ExpiresAt.HasValue && i.ExpiresAt >= ExpiresAtStart.Value);

            if (ExpiresAtEnd.HasValue)
                query = query.Where(i => i.ExpiresAt.HasValue && i.ExpiresAt <= ExpiresAtEnd.Value);

            // Filter by AcceptedAt date range
            if (AcceptedAtStart.HasValue)
                query = query.Where(i => i.AcceptedAt.HasValue && i.AcceptedAt >= AcceptedAtStart.Value);

            if (AcceptedAtEnd.HasValue)
                query = query.Where(i => i.AcceptedAt.HasValue && i.AcceptedAt <= AcceptedAtEnd.Value);

            // Apply sorting (default by CreatedAt)
            query = desc
                ? query.OrderByDescending(i => i.CreatedAt)
                : query.OrderBy(i => i.CreatedAt);

            PaginatedList<JudgeInvite> pagedResult = await judgeInviteRepo.GetPagingAsync(query, page, pageSize);

            // Use AutoMapper to map the items
            IReadOnlyCollection<JudgeInviteDTO> items = _mapper.Map<IReadOnlyCollection<JudgeInviteDTO>>(pagedResult.Items);

            return new PaginatedList<JudgeInviteDTO>(items, pagedResult.TotalCount, pagedResult.PageNumber, pagedResult.PageSize);
        }

        public async Task<JudgeInviteDTO> CreateAsync(
            Guid contestId,
            CreateJudgeInviteDTO dto)
        {
            try
            {
                var actorId = GetCurrentUserId();

                // Get repositories
                IGenericRepository<Contest> contestRepo = _uow.GetRepository<Contest>();
                IGenericRepository<User> userRepo = _uow.GetRepository<User>();
                IGenericRepository<JudgeInvite> inviteRepo = _uow.GetRepository<JudgeInvite>();
                IGenericRepository<Config> configRepo = _uow.GetRepository<Config>();

                // Get contest and ensure ownership
                Contest contest = await EnsureContestOwnedByOrganizerAsync(contestId, contestRepo);

                // Validate judge user
                User? judgeUser = await userRepo.Entities
                    .Where(u => u.UserId == dto.JudgeUserId && u.DeletedAt == null)
                    .FirstOrDefaultAsync()
                    ?? throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"No user with ID={dto.JudgeUserId}");

                if (judgeUser.Role != RoleConstants.Judge)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "User must have Judge role.");

                // Check if judge already has an active invite
                JudgeInvite? existingInvite = await inviteRepo.Entities
                    .Include(i => i.Contest)
                    .Include(i => i.Judge)
                    .FirstOrDefaultAsync(i => i.ContestId == contestId
                                           && i.JudgeId == dto.JudgeUserId
                                           && i.Status == Pending);

                if (existingInvite != null)
                {
                    // Refresh existing invite
                    DateTime now = DateTime.UtcNow;
                    int ttlDays = dto.TtlDays ?? await GetInviteTtlDaysAsync(configRepo, contestId);

                    // Update existing invite
                    existingInvite.ExpiresAt = now.AddDays(ttlDays);
                    existingInvite.InviteCode = GenerateInviteCode();
                    inviteRepo.Update(existingInvite);
                    await _uow.SaveAsync();

                    // Notify judge about the refreshed invite
                    await TryNotifyJudgeInvitationAsync(existingInvite.JudgeId, existingInvite);

                    // Log activity
                    if (actorId.HasValue)
                    {
                        await _logWriter.TryWriteAsync(actorId.Value,
                            ActivityActions.JudgeInviteResent,
                            TargetTypes.JudgeInvite,
                            existingInvite.InviteId.ToString());
                    }

                    // Use AutoMapper
                    return _mapper.Map<JudgeInviteDTO>(existingInvite);
                }

                // Check if judge is already assigned to this contest via Config
                bool alreadyAssigned = await IsJudgeAssignedToContestAsync(contestId, dto.JudgeUserId, configRepo);
                if (alreadyAssigned)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.EXISTED,
                        "Judge is already assigned to this contest.");

                // Get requester Id from HttpContext
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                // Create new invite
                JudgeInvite newInvite = new JudgeInvite
                {
                    InviteId = Guid.NewGuid(),
                    JudgeId = dto.JudgeUserId,
                    ContestId = contestId,
                    InviteCode = GenerateInviteCode(),
                    Status = Pending,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(dto.TtlDays ?? await GetInviteTtlDaysAsync(configRepo, contestId)),
                    CreatedBy = userId
                };

                await inviteRepo.InsertAsync(newInvite);
                await _uow.SaveAsync();


                // Load navigation properties for response
                JudgeInvite createdInvite = await inviteRepo.Entities
                    .Where(i => i.InviteId == newInvite.InviteId)
                    .Include(i => i.Contest)
                    .Include(i => i.Judge)
                    .AsNoTracking()
                    .FirstAsync();

                // Notify judge about the new invite
                await TryNotifyJudgeInvitationAsync(createdInvite.JudgeId, createdInvite);

                // Log activity
                if (actorId.HasValue)
                {
                    await _logWriter.TryWriteAsync(actorId.Value,
                        ActivityActions.JudgeInviteCreated,
                        TargetTypes.JudgeInvite,
                        createdInvite.InviteId.ToString());
                }

                // Use AutoMapper
                return _mapper.Map<JudgeInviteDTO>(createdInvite);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while creating the judge invite: " + ex.Message);
            }
        }

        public async Task<JudgeInviteDTO> ResendAsync(
            Guid contestId,
            Guid inviteId)
        {
            try
            {
                _uow.BeginTransaction();

                IGenericRepository<JudgeInvite> repo = _uow.GetRepository<JudgeInvite>();
                IGenericRepository<Config> configRepo = _uow.GetRepository<Config>();
                IGenericRepository<Contest> contestRepo = _uow.GetRepository<Contest>();

                await EnsureContestOwnedByOrganizerAsync(contestId, contestRepo);

                JudgeInvite? invite = await repo.Entities
                    .Include(i => i.Contest)
                    .Include(i => i.Judge)
                    .FirstOrDefaultAsync(i => i.InviteId == inviteId && i.ContestId == contestId)
                    ?? throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Invite not found.");

                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Only pending invites can be resent.");

                DateTime now = DateTime.UtcNow;
                int ttlDays = await GetInviteTtlDaysAsync(configRepo, invite.ContestId);

                // Get requester Id from HttpContext
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (!invite.ExpiresAt.HasValue || invite.ExpiresAt.Value <= now)
                {
                    // Expire old, create new
                    invite.Status = Expired;
                    repo.Update(invite);

                    // Create new invite
                    JudgeInvite newInvite = new JudgeInvite
                    {
                        InviteId = Guid.NewGuid(),
                        JudgeId = invite.JudgeId,
                        ContestId = invite.ContestId,
                        InviteCode = GenerateInviteCode(),
                        Status = Pending,
                        CreatedAt = now,
                        ExpiresAt = now.AddDays(ttlDays),
                        CreatedBy = userId
                    };

                    // Insert new invite
                    await repo.InsertAsync(newInvite);
                    await _uow.SaveAsync();

                    // Load additional properties
                    JudgeInvite createdInvite = await repo.Entities
                        .Where(i => i.InviteId == newInvite.InviteId)
                        .Include(i => i.Contest)
                        .Include(i => i.Judge)
                        .AsNoTracking()
                        .FirstAsync();

                    // Notify judge about the new invite
                    await TryNotifyJudgeInvitationAsync(createdInvite.JudgeId, createdInvite);

                    //  Log activity
                    var actorId = GetCurrentUserId();
                    if (actorId.HasValue)
                    {
                        await _logWriter.TryWriteAsync(actorId.Value,
                            ActivityActions.JudgeInviteResent,
                            TargetTypes.JudgeInvite,
                            createdInvite.InviteId.ToString());
                    }

                    // Use AutoMapper
                    _uow.CommitTransaction();
                    return _mapper.Map<JudgeInviteDTO>(createdInvite);
                }
                else
                {
                    // Refresh existing
                    invite.InviteCode = GenerateInviteCode();
                    invite.ExpiresAt = now.AddDays(ttlDays);
                    repo.Update(invite);
                    await _uow.SaveAsync();

                    // Notify judge about the refreshed invite
                    await TryNotifyJudgeInvitationAsync(invite.JudgeId, invite);

                    // Log activity
                    var actorId = GetCurrentUserId();
                    if (actorId.HasValue)
                    {
                        await _logWriter.TryWriteAsync(actorId.Value,
                            ActivityActions.JudgeInviteResent,
                            TargetTypes.JudgeInvite,
                            invite.InviteId.ToString());
                    }
                    // Use AutoMapper
                    _uow.CommitTransaction();
                    return _mapper.Map<JudgeInviteDTO>(invite);
                }
            }
            catch (Exception ex)
            {
                _uow.RollBack();
                if (ex is ErrorException)
                    throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while resending the invite: " + ex.Message);
            }
        }

        public async Task RevokeAsync(
            Guid contestId,
            Guid inviteId)
        {
            try
            {
                IGenericRepository<Contest> contestRepo = _uow.GetRepository<Contest>();
                await EnsureContestOwnedByOrganizerAsync(contestId, contestRepo);

                // Get invite
                IGenericRepository<JudgeInvite> repo = _uow.GetRepository<JudgeInvite>();
                var invite = await repo.Entities
                    .FirstOrDefaultAsync(i => i.InviteId == inviteId && i.ContestId == contestId)
                    ?? throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Invite not found.");

                // Only pending invites can be revoked
                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Only pending invites can be revoked.");

                // Revoke invite
                invite.Status = Revoked;
                repo.Update(invite);
                await _uow.SaveAsync();

                // Load navigation properties
                var inviteWithNav = await repo.Entities
                    .Where(i => i.InviteId == inviteId)
                    .Include(i => i.Contest)
                    .Include(i => i.Judge)
                    .AsNoTracking()
                    .FirstAsync();

                // Notify judge about the revoked invite
                await TryNotifyJudgeInvitationRevokedAsync(inviteWithNav.JudgeId, inviteWithNav);

                // Log activity
                var actorId = GetCurrentUserId();
                if (actorId.HasValue)
                {
                    await _logWriter.TryWriteAsync(actorId.Value,
                        ActivityActions.JudgeInviteRevoked,
                        TargetTypes.JudgeInvite,
                        inviteId.ToString());
                }

            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                    throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while revoking the invite: " + ex.Message);
            }
        }
        public async Task AcceptByCodeAsync(string inviteCode, string email)
        {
            try
            {
                _uow.BeginTransaction();

                var inviteRepo = _uow.GetRepository<JudgeInvite>();
                var configRepo = _uow.GetRepository<Config>();

                var (invite, _) = await ValidateInviteByCodeAsync(inviteCode, email, inviteRepo);

                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invite is not pending.");

                if (!invite.ExpiresAt.HasValue || invite.ExpiresAt.Value <= DateTime.UtcNow)
                {
                    invite.Status = Expired;
                    inviteRepo.Update(invite);
                    await _uow.SaveAsync();
                    throw new ErrorException(StatusCodes.Status410Gone, ResponseCodeConstants.GONE, "Invite has expired.");
                }

                bool alreadyAssigned = await IsJudgeAssignedToContestAsync(invite.ContestId, invite.JudgeId, configRepo);
                if (alreadyAssigned)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.EXISTED, "You are already assigned to this contest.");

                string configKey = ConfigKeys.ContestJudge(invite.ContestId, invite.JudgeId);
                var existingConfig = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == configKey);
                if (existingConfig == null)
                {
                    await configRepo.InsertAsync(new Config
                    {
                        Key = configKey,
                        Value = "active",
                        Scope = "contest",
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existingConfig.Value = "active";
                    existingConfig.Scope = "contest";
                    existingConfig.UpdatedAt = DateTime.UtcNow;
                    existingConfig.DeletedAt = null;
                    await configRepo.UpdateAsync(existingConfig);
                }

                invite.Status = Accepted;
                invite.AcceptedAt = DateTime.UtcNow;
                inviteRepo.Update(invite);

                await _uow.SaveAsync();
                _uow.CommitTransaction();

                // log activity
                await _logWriter.TryWriteAsync(invite.JudgeId,
                    ActivityActions.JudgeInviteAccepted,
                    TargetTypes.JudgeInvite,
                    invite.InviteId.ToString());

                // notify inviter
                var inviterId = TryParseGuid(invite.CreatedBy);
                if (inviterId.HasValue)
                {
                    await TryNotifyInviterAcceptedAsync(inviterId.Value, invite);
                }
            }
            catch (Exception ex)
            {
                _uow.RollBack();
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while accepting the invite: " + ex.Message);
            }
        }

        public async Task DeclineByCodeAsync(string inviteCode, string email)
        {
            try
            {
                var inviteRepo = _uow.GetRepository<JudgeInvite>();
                var (invite, normEmail) = await ValidateInviteByCodeAsync(inviteCode, email, inviteRepo);

                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status409Conflict, ResponseCodeConstants.CONFLICT, "Invite is not pending.");

                invite.Status = Declined;
                inviteRepo.Update(invite);
                await _uow.SaveAsync();

                // log activity
                await _logWriter.TryWriteAsync(invite.JudgeId,
                    ActivityActions.JudgeInviteDeclined,
                    TargetTypes.JudgeInvite,
                    invite.InviteId.ToString());

                // notify inviter
                var inviterId = TryParseGuid(invite.CreatedBy);
                if (inviterId.HasValue)
                {
                    await TryNotifyInviterDeclinedAsync(inviterId.Value, invite, normEmail);
                }
            }
            catch (Exception ex)
            {
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while declining the invite: " + ex.Message);
            }

        }


        private static async Task<bool> IsJudgeAssignedToContestAsync(
            Guid contestId,
            Guid judgeUserId,
            IGenericRepository<Config> configRepo)
        {
            string configKey = ConfigKeys.ContestJudge(contestId, judgeUserId);
            var config = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == configKey && c.DeletedAt == null);

            return config != null && config.Value?.ToLower() == "active";
        }

        private static async Task<int> GetInviteTtlDaysAsync(IGenericRepository<Config> configRepo, Guid contestId)
        {
            // Per-contest override
            string? inviteTtlDayOfContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestInviteTtlDays(contestId) && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(inviteTtlDayOfContest, out int inviteTtlDays) && inviteTtlDays >= 1) return inviteTtlDays;

            // Global default
            string? global = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamInviteTtlDays && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(global, out int defaultInviteTtlDays) && defaultInviteTtlDays >= 1) return defaultInviteTtlDays;

            // Hard fallback
            return 7;
        }

        private static string GenerateInviteCode()
        {
            // Generate 8-character alphanumeric code
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Random random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public async Task<PaginatedList<JudgeWithInviteStatusDTO>> GetJudgesWithInviteStatusAsync(
            Guid contestId,
            int page,
            int pageSize,
            string? judgeNameSearch,
            string? judgeEmailSearch,
            JudgeInviteStatusEnum? inviteStatus,
            bool? hasBeenInvited,
            string sortBy,
            bool desc)
        {
            try
            {
                var contestRepo = _uow.GetRepository<Contest>();
                await EnsureContestOwnedByOrganizerAsync(contestId, contestRepo);

                // Validate pagination parameters
                if (page < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                if (pageSize > 100)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page size cannot exceed 100.");
                }

                // Get user repository
                IGenericRepository<User> userRepo = _uow.GetRepository<User>();

                // Get judge invite repository
                IGenericRepository<JudgeInvite> inviteRepo = _uow.GetRepository<JudgeInvite>();

                // Get all judges with Judge role
                IQueryable<User> judgeQuery = userRepo.Entities
                    .Where(u => u.Role == RoleConstants.Judge && !u.DeletedAt.HasValue);

                // Apply filters on judge name
                if (!string.IsNullOrWhiteSpace(judgeNameSearch))
                {
                    var name = judgeNameSearch.Trim();
                    judgeQuery = judgeQuery.Where(j => j.Fullname.Contains(name));
                }

                // Apply filters on judge email
                if (!string.IsNullOrWhiteSpace(judgeEmailSearch))
                {
                    var email = judgeEmailSearch.Trim();
                    judgeQuery = judgeQuery.Where(j => j.Email.Contains(email));
                }

                var latestInvites = inviteRepo.Entities
                    .Where(i => i.ContestId == contestId)
                    .GroupBy(i => i.JudgeId)
                    .Select(g => g.OrderByDescending(x => x.CreatedAt).FirstOrDefault());

                var query = from judge in judgeQuery
                            join invite in latestInvites on judge.UserId equals invite!.JudgeId into gj
                            from invite in gj.DefaultIfEmpty()
                            select new JudgeWithInviteStatusDTO
                            {
                                JudgeId = judge.UserId,
                                JudgeName = judge.Fullname,
                                JudgeEmail = judge.Email,
                                JudgeStatus = judge.Status,
                                InviteId = invite != null ? invite.InviteId : null,
                                InviteStatus = invite != null ? invite.Status : null,
                                InvitedAt = invite != null ? invite.CreatedAt : null,
                                ExpiresAt = invite != null ? invite.ExpiresAt : null,
                                AcceptedAt = invite != null ? invite.AcceptedAt : null,
                                InviteCode = invite != null ? invite.InviteCode : null
                            };

                // Filter by invitation status
                if (inviteStatus.HasValue)
                {
                    string statusString = inviteStatus.Value.ToString().ToLowerInvariant();
                    query = query.Where(j => j.InviteStatus == statusString);
                }

                // Filter by whether judge has been invited
                if (hasBeenInvited.HasValue)
                {
                    query = hasBeenInvited.Value
                        ? query.Where(j => j.InviteId != null)
                        : query.Where(j => j.InviteId == null);
                }

                // Apply sorting
                query = (sortBy?.ToLowerInvariant()) switch
                {
                    "name" or "judgename" => desc
                        ? query.OrderByDescending(j => j.JudgeName)
                        : query.OrderBy(j => j.JudgeName),
                    "email" or "judgeemail" => desc
                        ? query.OrderByDescending(j => j.JudgeEmail)
                        : query.OrderBy(j => j.JudgeEmail),
                    "invitedat" => desc
                        ? query.OrderByDescending(j => j.InvitedAt)
                        : query.OrderBy(j => j.InvitedAt),
                    "status" or "invitestatus" => desc
                        ? query.OrderByDescending(j => j.InviteStatus)
                        : query.OrderBy(j => j.InviteStatus),
                    "expiresat" => desc
                        ? query.OrderByDescending(j => j.ExpiresAt)
                        : query.OrderBy(j => j.ExpiresAt),
                    "acceptedat" => desc
                        ? query.OrderByDescending(j => j.AcceptedAt)
                        : query.OrderBy(j => j.AcceptedAt),
                    _ => desc
                        ? query.OrderByDescending(j => j.JudgeName)
                        : query.OrderBy(j => j.JudgeName),
                };

                int totalCount = await query.CountAsync();

                List<JudgeWithInviteStatusDTO> paginatedItems = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedList<JudgeWithInviteStatusDTO>(paginatedItems, totalCount, page, pageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving judges with invite status: {ex.Message}");
            }
        }

        private Guid? GetCurrentUserId()
        {
            var str = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(str, out var g)) return g;
            return null;
        }

        private Guid GetCurrentUserIdOrThrow()
        {
            var str = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(str, out var g)) return g;
            throw new ErrorException(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "User not authenticated.");
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        private static Guid? TryParseGuid(string? str)
        {
            if (Guid.TryParse(str, out var g)) return g;
            return null;
        }

        private async Task<Contest> EnsureContestOwnedByOrganizerAsync(Guid contestId, IGenericRepository<Contest> contestRepo)
        {
            var userId = GetCurrentUserIdOrThrow();

            var contest = await contestRepo.Entities
                .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"No contest with ID={contestId}");

            if (!string.Equals(contest.CreatedBy, userId.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN, "You do not have permission to manage judge invites for this contest.");

            return contest;
        }

        private async Task<(JudgeInvite Invite, string NormalizedEmail)> ValidateInviteByCodeAsync(
            string inviteCode,
            string email,
            IGenericRepository<JudgeInvite> inviteRepo)
        {
            if (string.IsNullOrWhiteSpace(inviteCode) || string.IsNullOrWhiteSpace(email))
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "InviteCode and Email are required.");

            var normEmail = NormalizeEmail(email);

            var invite = await inviteRepo.Entities
                .Where(i => i.InviteCode == inviteCode)
                .Include(i => i.Contest)
                .Include(i => i.Judge)
                .FirstOrDefaultAsync();

            if (invite == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Invalid invite code.");

            if (invite.Judge == null || invite.Judge.DeletedAt.HasValue)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Judge not found.");

            if (invite.Judge.Role != RoleConstants.Judge)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "User must have Judge role.");

            if (!string.Equals(NormalizeEmail(invite.Judge.Email), normEmail, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN, "This invite does not belong to your email.");

            return (invite, normEmail);
        }

        private async Task TryNotifyJudgeInvitationAsync(Guid judgeUserId, JudgeInvite invite)
        {
            try
            {
                var email = invite.Judge?.Email?.Trim().ToLowerInvariant();

                await _notificationService.CreateInAppToUserAsync(judgeUserId, NotificationTypes.JudgeInvitation, new
                {
                    inviteId = invite.InviteId,
                    inviteCode = invite.InviteCode,
                    status = invite.Status,
                    expiresAt = invite.ExpiresAt,

                    contestId = invite.ContestId,
                    contestName = invite.Contest?.Name,

                    judgeId = invite.JudgeId,
                    judgeName = invite.Judge?.Fullname,
                    judgeEmail = email,

                    targetType = TargetTypes.JudgeInvite,
                    targetId = invite.InviteId.ToString(),

                    actions = new
                    {
                        accept = new { method = "POST", url = "/api/judge-invites/accept", query = new { inviteCode = invite.InviteCode, email } },
                        decline = new { method = "POST", url = "/api/judge-invites/decline", query = new { inviteCode = invite.InviteCode, email } }
                    },

                    message = $"You have been invited to judge contest '{invite.Contest?.Name}'."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send judge invitation notification. InviteId={InviteId}, ContestId={ContestId}, JudgeId={JudgeId}",
                    invite.InviteId, invite.ContestId, judgeUserId);
            }
        }

        private async Task TryNotifyJudgeInvitationRevokedAsync(Guid judgeUserId, JudgeInvite invite)
        {
            try
            {
                await _notificationService.CreateInAppToUserAsync(judgeUserId, NotificationTypes.JudgeInvitationRevoked, new
                {
                    inviteId = invite.InviteId,
                    contestId = invite.ContestId,
                    contestName = invite.Contest?.Name,

                    targetType = TargetTypes.JudgeInvite,
                    targetId = invite.InviteId.ToString(),

                    message = $"Your judge invitation for contest '{invite.Contest?.Name}' has been revoked."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send judge invitation revoked notification. InviteId={InviteId}, JudgeId={JudgeId}",
                    invite.InviteId, judgeUserId);
            }
        }

        private async Task TryNotifyInviterAcceptedAsync(Guid inviterUserId, JudgeInvite invite)
        {
            try
            {
                await _notificationService.CreateInAppToUserAsync(inviterUserId, NotificationTypes.JudgeInvitationAccepted, new
                {
                    inviteId = invite.InviteId,
                    contestId = invite.ContestId,
                    contestName = invite.Contest?.Name,

                    judgeId = invite.JudgeId,
                    judgeName = invite.Judge?.Fullname,
                    judgeEmail = invite.Judge?.Email,

                    acceptedAt = invite.AcceptedAt,
                    targetType = TargetTypes.JudgeInvite,
                    targetId = invite.InviteId.ToString(),

                    message = $"{invite.Judge?.Fullname} accepted your judge invitation."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send judge invite accepted notification. InviteId={InviteId}, InviterUserId={InviterUserId}",
                    invite.InviteId, inviterUserId);
            }
        }

        private async Task TryNotifyInviterDeclinedAsync(Guid inviterUserId, JudgeInvite invite, string normEmail)
        {
            try
            {
                await _notificationService.CreateInAppToUserAsync(inviterUserId, NotificationTypes.JudgeInvitationDenied, new
                {
                    inviteId = invite.InviteId,
                    contestId = invite.ContestId,
                    contestName = invite.Contest?.Name,

                    judgeId = invite.JudgeId,
                    judgeName = invite.Judge?.Fullname,
                    judgeEmail = normEmail,

                    targetType = TargetTypes.JudgeInvite,
                    targetId = invite.InviteId.ToString(),

                    message = $"{invite.Judge?.Fullname} declined your judge invitation."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send judge invite denied notification. InviteId={InviteId}, InviterUserId={InviterUserId}",
                    invite.InviteId, inviterUserId);
            }
        }

    }
}
