using Repository.DTOs.JudgeInviteDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IJudgeInviteService
    {
        Task<PaginatedList<JudgeInviteDTO>> GetForContestAsync(
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
            bool desc);

        Task<JudgeInviteDTO> CreateAsync(Guid contestId, CreateJudgeInviteDTO dto);

        Task<JudgeInviteDTO> ResendAsync(Guid contestId, Guid inviteId);

        Task RevokeAsync(Guid contestId, Guid inviteId);

        Task AcceptByCodeAsync(string inviteCode);
        Task DeclineByCodeAsync(string inviteCode);
    }
}
