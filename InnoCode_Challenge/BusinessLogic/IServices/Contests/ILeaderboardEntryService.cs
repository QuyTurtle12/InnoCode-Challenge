using Repository.DTOs.LeaderboardEntryDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface ILeaderboardEntryService
    {
        Task<PaginatedList<GetLeaderboardEntryDTO>> GetPaginatedLeaderboardAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? contestNameSearch);
        Task AddTeamToLeaderboardAsync(Guid contestId, Guid teamId);
        Task UpdateLeaderboardAsync(Guid contestId);
        Task UpdateTeamScoreAsync(Guid contestId, Guid teamId, double newScore);
        Task AddScoreToTeamAsync(Guid contestId, Guid teamId, double scoreToAdd);
        Task RecalculateRanksAsync(Guid contestId);
        Task<string> ToggleLeaderboardFreezeAsync(Guid contestId);
        Task ApplyEliminationAsync(Guid contestId, Guid roundId);

    }
}
