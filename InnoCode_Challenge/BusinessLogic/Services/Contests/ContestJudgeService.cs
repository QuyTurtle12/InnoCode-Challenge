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

        public async Task ParticipateAsync(JudgeParticipateContestDTO dto)
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

        public async Task LeaveAsync(JudgeParticipateContestDTO dto)
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
    }
}