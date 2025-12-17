using Repository.DTOs.TeamDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Students
{
    public interface ITeamService
    {
        Task<PaginatedList<TeamWithMembersDTO>> GetAsync(
            int pageNumber,
            int pageSize,
            Guid? contestIdSearch,
            Guid? schoolIdSearch,
            Guid? mentorIdSearch,
            string? nameSearch,
            bool IsMyTeam);
        Task<TeamWithMembersDTO> GetByIdAsync(Guid id);
        Task<TeamDTO> CreateAsync(CreateTeamDTO dto);
        Task<TeamDTO> UpdateAsync(Guid id, UpdateTeamDTO dto);
        Task DeleteAsync(Guid id);

        Task<IReadOnlyList<TeamWithMembersDTO>> GetMyTeamsAsync();

    }
}
