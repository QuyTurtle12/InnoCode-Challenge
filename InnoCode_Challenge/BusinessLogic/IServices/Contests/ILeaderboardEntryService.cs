using Repository.DTOs.LeaderboardEntryDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface ILeaderboardEntryService
    {
        Task<PaginatedList<GetLeaderboardEntryDTO>> GetPaginatedLeaderboardAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? contestNameSearch);
        Task CreateLeaderboardAsync(CreateLeaderboardEntryDTO LeaderboardDTO);
        Task UpdateLeaderboardAsync(UpdateLeaderboardEntryDTO LeaderboardDTO);
        //Task UpdateTeamScoreAsync(Guid contestId, Guid teamId, double newScore);

    }
}
