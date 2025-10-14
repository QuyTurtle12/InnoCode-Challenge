using Repository.DTOs.TeamMemberDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface ITeamMemberService
    {
        Task<PaginatedList<TeamMemberDTO>> GetAsync(TeamMemberQueryParams queryParams);
        Task<TeamMemberDTO> GetByIdAsync(Guid teamId, Guid studentId);
        Task<TeamMemberDTO> CreateAsync(CreateTeamMemberDTO dto);
        Task<TeamMemberDTO> UpdateAsync(Guid teamId, Guid studentId, UpdateTeamMemberDTO dto);
        Task DeleteAsync(Guid teamId, Guid studentId);
    }
}
