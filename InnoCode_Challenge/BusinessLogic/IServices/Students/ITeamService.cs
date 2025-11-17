using Repository.DTOs.TeamDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Students
{
    public interface ITeamService
    {
        Task<PaginatedList<TeamDTO>> GetAsync(TeamQueryParams queryParams);
        Task<TeamDTO> GetByIdAsync(Guid id);
        Task<TeamDTO> CreateAsync(CreateTeamDTO dto);
        Task<TeamDTO> UpdateAsync(Guid id, UpdateTeamDTO dto);
        Task DeleteAsync(Guid id);

        Task<IReadOnlyList<TeamWithMembersDTO>> GetMyTeamsAsync();

    }
}
