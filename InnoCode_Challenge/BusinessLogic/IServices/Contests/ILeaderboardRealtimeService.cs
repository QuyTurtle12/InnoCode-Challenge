using Repository.DTOs.LeaderboardEntryDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface ILeaderboardRealtimeService
    {
        Task NotifyLeaderboardUpdatedAsync(Guid contestId);
    }
}
