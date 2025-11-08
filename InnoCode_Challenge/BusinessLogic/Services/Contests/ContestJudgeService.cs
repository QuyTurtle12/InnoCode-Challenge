using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ContestDTOs;
using Repository.IRepositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Utility.Constant;
using Utility.ExceptionCustom;

namespace BusinessLogic.Services.Contests
{
    public class ContestJudgeService : IContestJudgeService
    {
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ContestJudgeService(
            IUOW unitOfWork,
            IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task ParticipateAsync(JudgeContestDTO dto)
        {
            var judge = await GetCurrentJudgeAsync();

            var configRepo = _unitOfWork.GetRepository<Config>();
            var now = DateTime.UtcNow;

            var key = ConfigKeys.ContestJudge(dto.ContestId, judge.UserId);

            var existing = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == key);

            if (existing != null)
            {
                if (existing.DeletedAt != null)
                {
                    existing.DeletedAt = null;
                    existing.UpdatedAt = now;
                    await _unitOfWork.SaveAsync();
                }

                return;
            }

            var config = new Config
            {
                Key = key,
                Value = "judge",      
                Scope = "contest",    
                UpdatedAt = now,
                DeletedAt = null
            };

            await configRepo.InsertAsync(config);
            await _unitOfWork.SaveAsync();
        }

        public async Task LeaveAsync(JudgeContestDTO dto)
        {
            var judge = await GetCurrentJudgeAsync();

            var configRepo = _unitOfWork.GetRepository<Config>();
            var now = DateTime.UtcNow;

            var key = ConfigKeys.ContestJudge(dto.ContestId, judge.UserId);

            var existing = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == key);

            if (existing == null)
            {
                return;
            }

            if (existing.DeletedAt == null)
            {
                existing.DeletedAt = now;
                existing.UpdatedAt = now;
                await _unitOfWork.SaveAsync();
            }
        }

        private async Task<User> GetCurrentJudgeAsync()
        {
            var httpUser = _httpContextAccessor.HttpContext?.User
                ?? throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var idStr = httpUser.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? httpUser.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var userId))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            var userRepo = _unitOfWork.GetRepository<User>();
            var user = await userRepo.GetByIdAsync(userId)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (!string.Equals(user.Role, RoleConstants.Judge, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only judges can perform this action.");

            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "USER_INACTIVE", "Your account is not active.");

            return user;
        }

        public async Task<IList<JudgeInContestDTO>> GetJudgesByContestAsync(Guid contestId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            var userRepo = _unitOfWork.GetRepository<User>();

            var prefix = ConfigKeys.ContestJudge(contestId, Guid.Empty);
            var basePrefix = prefix[..prefix.LastIndexOf(':')];

            var configs = await configRepo.Entities
                .Where(c =>
                    c.Scope == "contest" &&
                    c.DeletedAt == null &&
                    c.Key.StartsWith(basePrefix))
                .ToListAsync();

            var judgeIds = configs
                .Select(c => ExtractJudgeIdFromKey(c.Key))
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (!judgeIds.Any())
                return new List<JudgeInContestDTO>();

            var judges = await userRepo.Entities
                .Where(u => judgeIds.Contains(u.UserId) && u.DeletedAt == null)
                .ToListAsync();

            return judges
                .Select(u => new JudgeInContestDTO
                {
                    UserId = u.UserId,
                    FullName = u.Fullname,
                    Email = u.Email,
                    Status = u.Status
                })
                .ToList();
        }

        public async Task<IList<JudgeContestDTO>> GetContestsOfCurrentJudgeAsync()
        {
            var judge = await GetCurrentJudgeAsync();
            return await GetContestsOfJudgeAsync(judge.UserId);
        }

        public async Task<IList<JudgeContestDTO>> GetContestsOfJudgeAsync(Guid judgeUserId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();

            var suffix = $":judge:{judgeUserId}";

            var configs = await configRepo.Entities
                .Where(c =>
                    c.Scope == "contest" &&
                    c.DeletedAt == null &&
                    c.Key.StartsWith("contest:") &&
                    c.Key.EndsWith(suffix))
                .ToListAsync();

            var result = new List<JudgeContestDTO>();

            foreach (var c in configs)
            {
                var contestId = ExtractContestIdFromKey(c.Key, judgeUserId);
                if (contestId != Guid.Empty)
                {
                    result.Add(new JudgeContestDTO { ContestId = contestId });
                }
            }

            return result;
        }

        private static Guid ExtractJudgeIdFromKey(string key)
        {
            var lastColon = key.LastIndexOf(':');
            if (lastColon < 0 || lastColon == key.Length - 1) return Guid.Empty;

            var part = key[(lastColon + 1)..];
            return Guid.TryParse(part, out var id) ? id : Guid.Empty;
        }

        private static Guid ExtractContestIdFromKey(string key, Guid judgeUserId)
        {
            var suffix = $":judge:{judgeUserId}";
            if (!key.StartsWith("contest:") || !key.EndsWith(suffix))
                return Guid.Empty;

            // remove "contest:" prefix and ":judge:{judgeUserId}" suffix
            var withoutPrefix = key["contest:".Length..];
            var index = withoutPrefix.IndexOf(":judge:", StringComparison.Ordinal);
            if (index <= 0) return Guid.Empty;

            var contestIdStr = withoutPrefix[..index];
            return Guid.TryParse(contestIdStr, out var id) ? id : Guid.Empty;
        }

    }
}