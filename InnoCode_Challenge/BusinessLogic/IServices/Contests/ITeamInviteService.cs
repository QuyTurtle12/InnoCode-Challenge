using Repository.DTOs.TeamInviteDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface ITeamInviteService
    {
        Task<PaginatedList<TeamInviteDTO>> GetForTeamAsync(Guid teamId, TeamInviteQueryParams query,
            Guid requesterUserId, string requesterRole);

        Task<TeamInviteCreatedDTO> CreateAsync(Guid teamId, CreateTeamInviteDTO dto,
            Guid invitedByUserId, string invitedByRole);

        Task<TeamInviteCreatedDTO> ResendAsync(Guid teamId, Guid inviteId,
            Guid requesterUserId, string requesterRole);


        Task RevokeAsync(Guid teamId, Guid inviteId,
            Guid requesterUserId, string requesterRole);

        Task AcceptByTokenAsync(string token, string email);
        Task DeclineByTokenAsync(string token, string email);
    }
}
