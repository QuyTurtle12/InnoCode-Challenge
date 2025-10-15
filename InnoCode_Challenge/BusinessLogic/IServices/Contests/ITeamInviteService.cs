using Repository.DTOs.TeamInviteDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface ITeamInviteService
    {
        Task<PaginatedList<TeamInviteDTO>> GetForTeamAsync(Guid teamId, TeamInviteQueryParams query,
            Guid requesterUserId, string requesterRole);

        Task<TeamInviteDTO> CreateAsync(Guid teamId, CreateTeamInviteDTO dto,
            Guid invitedByUserId, string invitedByRole);

        Task<TeamInviteDTO> ResendAsync(Guid teamId, Guid inviteId,
            Guid requesterUserId, string requesterRole);

        Task RevokeAsync(Guid teamId, Guid inviteId,
            Guid requesterUserId, string requesterRole);

        Task AcceptByTokenAsync(string token, Guid currentUserId);
        Task DeclineByTokenAsync(string token, Guid currentUserId);
    }
}
