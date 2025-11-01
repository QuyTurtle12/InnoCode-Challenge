using Repository.DTOs.LeaderboardEntryDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface ILeaderboardRealtimeService
    {
        Task BroadcastLeaderboardUpdateAsync(Guid contestId, IList<TeamInfo> leaderboard);
    }
}
