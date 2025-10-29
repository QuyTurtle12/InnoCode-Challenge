using Repository.DTOs.LeaderboardEntryDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface ILeaderboardRealtimeService
    {
        Task BroadcastLeaderboardUpdateAsync(Guid contestId, IList<TeamInfo> leaderboard);
        Task NotifyScoreUpdateAsync(Guid contestId, Guid teamId, double newScore, int newRank);
    }
}
