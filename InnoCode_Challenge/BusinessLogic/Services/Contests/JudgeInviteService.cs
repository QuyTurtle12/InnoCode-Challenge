using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

        private const string Pending = "pending";
        private const string Accepted = "accepted";
        private const string Declined = "declined";
        private const string Revoked = "revoked";
        private const string Expired = "expired";

        public JudgeInviteService(IUOW uow, IMapper mapper, IHttpContextAccessor httpContextAccessor)
        {
            _uow = uow;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
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
                string statusString = status.Value.ToString().ToLower();
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
                judgeNameSearch = judgeNameSearch.Trim().ToLower();
                query = query.Where(i => i.Judge.Fullname.ToLower().Contains(judgeNameSearch));
            }

            // Filter by judge email
            if (!string.IsNullOrWhiteSpace(judgeEmailSearch))
            {
                judgeEmailSearch = judgeEmailSearch.Trim().ToLower();
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
                // Get repositories
                IGenericRepository<Contest> contestRepo = _uow.GetRepository<Contest>();
                IGenericRepository<User> userRepo = _uow.GetRepository<User>();
                IGenericRepository<JudgeInvite> inviteRepo = _uow.GetRepository<JudgeInvite>();
                IGenericRepository<Config> configRepo = _uow.GetRepository<Config>();

                // Get contest
                Contest? contest = await contestRepo.Entities
                    .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                    .FirstOrDefaultAsync()
                    ?? throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"No contest with ID={contestId}");

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
                IGenericRepository<JudgeInvite> repo = _uow.GetRepository<JudgeInvite>();
                IGenericRepository<Config> configRepo = _uow.GetRepository<Config>();

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

                if (invite.ExpiresAt <= now)
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

                    // Use AutoMapper
                    return _mapper.Map<JudgeInviteDTO>(createdInvite);
                }
                else
                {
                    // Refresh existing
                    invite.InviteCode = GenerateInviteCode();
                    invite.ExpiresAt = now.AddDays(ttlDays);
                    repo.Update(invite);
                    await _uow.SaveAsync();

                    // Use AutoMapper
                    return _mapper.Map<JudgeInviteDTO>(invite);
                }
            }
            catch (Exception ex)
            {
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
                // Get invite
                IGenericRepository<JudgeInvite> repo = _uow.GetRepository<JudgeInvite>();
                JudgeInvite? invite = await repo.Entities
                    .Where(i => i.InviteId == inviteId && i.ContestId == contestId)
                    .FirstOrDefaultAsync();

                // Validate invite
                if (invite == null)
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Invite not found.");

                // Only pending invites can be revoked
                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Only pending invites can be revoked.");

                // Revoke invite
                invite.Status = Revoked;
                repo.Update(invite);
                await _uow.SaveAsync();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                    throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while revoking the invite: " + ex.Message);
            }
        }

        public async Task AcceptByCodeAsync(string inviteCode)
        {
            try
            {
                // Get current user ID from HttpContext
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "User not authenticated.");

                // Validate user ID format
                if (!Guid.TryParse(userId, out Guid currentUserId))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid user ID.");

                // Get repositories
                IGenericRepository<JudgeInvite> inviteRepo = _uow.GetRepository<JudgeInvite>();
                IGenericRepository<User> userRepo = _uow.GetRepository<User>();
                IGenericRepository<Config> configRepo = _uow.GetRepository<Config>();

                // Get invite by code
                JudgeInvite? invite = await inviteRepo.Entities
                    .Where(i => i.InviteCode == inviteCode)
                    .Include(i => i.Contest)
                    .Include(i => i.Judge)
                    .FirstOrDefaultAsync();

                // Validate invite
                if (invite == null)
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND,
                        "Invalid invite code.");

                // Check invite status
                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Invite is not pending.");

                // Check expiration
                if (invite.ExpiresAt <= DateTime.UtcNow)
                {
                    // Mark invite as expired
                    invite.Status = Expired;
                    inviteRepo.Update(invite);
                    await _uow.SaveAsync();
                    throw new ErrorException(StatusCodes.Status410Gone, ResponseCodeConstants.GONE,
                        "Invite has expired.");
                }

                // Check if invite is for the current user
                if (currentUserId != invite.JudgeId)
                    throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                        "This invite is for a different judge.");

                // Check if judge is already assigned
                bool alreadyAssigned = await IsJudgeAssignedToContestAsync(invite.ContestId, currentUserId, configRepo);
                if (alreadyAssigned)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.EXISTED,
                        "You are already assigned to this contest.");

                // Assign judge to contest via Config
                string configKey = ConfigKeys.ContestJudge(invite.ContestId, currentUserId);
                Config config = new Config
                {
                    Key = configKey,
                    Value = "active",
                    Scope = "contest",
                    UpdatedAt = DateTime.UtcNow
                };

                // Insert config
                await configRepo.InsertAsync(config);

                // Update invite status to accepted
                invite.Status = Accepted;
                invite.AcceptedAt = DateTime.UtcNow;
                inviteRepo.Update(invite);

                await _uow.SaveAsync();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                    throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "An error occurred while accepting the invite: " + ex.Message);
            }   
        }

        public async Task DeclineByCodeAsync(string inviteCode)
        {
            try
            {
                // Get current user ID from HttpContext
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "User not authenticated.");

                // Validate user ID format
                if (!Guid.TryParse(userId, out Guid currentUserId))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid user ID.");

                // Get repositories
                IGenericRepository<JudgeInvite> inviteRepo = _uow.GetRepository<JudgeInvite>();
                IGenericRepository<User> userRepo = _uow.GetRepository<User>();

                // Get invite by code
                JudgeInvite? invite = await inviteRepo.Entities
                    .Where(i => i.InviteCode == inviteCode)
                    .FirstOrDefaultAsync();

                // Validate invite
                if (invite == null)
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND,
                        "Invalid invite code.");

                // Check invite status
                if (invite.Status != Pending)
                    throw new ErrorException(StatusCodes.Status409Conflict, ResponseCodeConstants.CONFLICT,
                        "Invite is not pending.");

                // Check if invite is for the current user
                if (currentUserId != invite.JudgeId)
                    throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                        "This invite is for a different judge.");

                // Update invite status to declined
                invite.Status = Declined;
                inviteRepo.Update(invite);
                await _uow.SaveAsync();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                    throw;

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
                // Validate pagination parameters
                if (page < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                if (pageSize > 100)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page size cannot exceed 100.");
                }

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _uow.GetRepository<Contest>();

                // Verify contest exists
                Contest? contest = await contestRepo.Entities
                    .Where(c => c.ContestId == contestId && !c.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                if (contest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"No contest found with ID={contestId}");
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
                    judgeQuery = judgeQuery.Where(j => j.Fullname.Contains(judgeNameSearch));
                }

                // Apply filters on judge email
                if (!string.IsNullOrWhiteSpace(judgeEmailSearch))
                {
                    judgeQuery = judgeQuery.Where(j => j.Email.Contains(judgeEmailSearch));
                }

                // Get all judges
                List<User> allJudges = await judgeQuery
                    .ToListAsync();

                // Extract judge IDs
                List<Guid> judgeIds = allJudges.Select(j => j.UserId).ToList();

                // Get all invites for these judges in this contest
                List<JudgeInvite> invites = await inviteRepo.Entities
                    .Where(i => i.ContestId == contestId && judgeIds.Contains(i.JudgeId))
                    .ToListAsync();

                // Create a dictionary for faster lookup
                Dictionary<Guid, JudgeInvite> inviteLookup = invites.ToDictionary(i => i.JudgeId);

                // Map judges to DTOs with invite status
                List<JudgeWithInviteStatusDTO> judgeWithInvites = allJudges.Select(judge =>
                {
                    // Check if this judge has an invite
                    inviteLookup.TryGetValue(judge.UserId, out JudgeInvite? invite);

                    return new JudgeWithInviteStatusDTO
                    {
                        JudgeId = judge.UserId,
                        JudgeName = judge.Fullname,
                        JudgeEmail = judge.Email,
                        JudgeStatus = judge.Status,
                        InviteId = invite?.InviteId,
                        InviteStatus = invite?.Status,
                        InvitedAt = invite?.CreatedAt,
                        ExpiresAt = invite?.ExpiresAt,
                        AcceptedAt = invite?.AcceptedAt,
                        InviteCode = invite?.InviteCode
                    };
                }).ToList();

                // Filter by invitation status
                if (inviteStatus.HasValue)
                {
                    string statusString = inviteStatus.Value.ToString().ToLower();
                    judgeWithInvites = judgeWithInvites.Where(j => j.InviteStatus == statusString).ToList();
                }

                // Filter by whether judge has been invited
                if (hasBeenInvited.HasValue)
                {
                    if (hasBeenInvited.Value)
                    {
                        // Only show judges who have been invited
                        judgeWithInvites = judgeWithInvites.Where(j => j.InviteId != null).ToList();
                    }
                    else
                    {
                        // Only show judges who have NOT been invited
                        judgeWithInvites = judgeWithInvites.Where(j => j.InviteId == null).ToList();
                    }
                }

                // Apply sorting
                judgeWithInvites = (sortBy?.ToLowerInvariant()) switch
                {
                    "name" or "judgename" => desc
                        ? judgeWithInvites.OrderByDescending(j => j.JudgeName).ToList()
                        : judgeWithInvites.OrderBy(j => j.JudgeName).ToList(),
                    "email" or "judgeemail" => desc
                        ? judgeWithInvites.OrderByDescending(j => j.JudgeEmail).ToList()
                        : judgeWithInvites.OrderBy(j => j.JudgeEmail).ToList(),
                    "invitedat" => desc
                        ? judgeWithInvites.OrderByDescending(j => j.InvitedAt).ToList()
                        : judgeWithInvites.OrderBy(j => j.InvitedAt).ToList(),
                    "status" or "invitestatus" => desc
                        ? judgeWithInvites.OrderByDescending(j => j.InviteStatus).ToList()
                        : judgeWithInvites.OrderBy(j => j.InviteStatus).ToList(),
                    "expiresat" => desc
                        ? judgeWithInvites.OrderByDescending(j => j.ExpiresAt).ToList()
                        : judgeWithInvites.OrderBy(j => j.ExpiresAt).ToList(),
                    "acceptedat" => desc
                        ? judgeWithInvites.OrderByDescending(j => j.AcceptedAt).ToList()
                        : judgeWithInvites.OrderBy(j => j.AcceptedAt).ToList(),
                    _ => desc
                        ? judgeWithInvites.OrderByDescending(j => j.JudgeName).ToList()
                        : judgeWithInvites.OrderBy(j => j.JudgeName).ToList(),
                };

                // Get total count after filtering
                int totalCount = judgeWithInvites.Count;

                // Apply pagination
                List<JudgeWithInviteStatusDTO> paginatedItems = judgeWithInvites
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Create paginated list
                PaginatedList<JudgeWithInviteStatusDTO> paginatedList = new PaginatedList<JudgeWithInviteStatusDTO>(paginatedItems, totalCount, page, pageSize);

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
                    $"Error retrieving judges with invite status: {ex.Message}");
            }
        }
    }
}