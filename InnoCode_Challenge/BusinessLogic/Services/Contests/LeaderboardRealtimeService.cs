using BusinessLogic.Hubs;
using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Services.Contests
{
    public class LeaderboardRealtimeService : ILeaderboardRealtimeService
    {
        private readonly IHubContext<LeaderboardHub> _hubContext;

        public LeaderboardRealtimeService(IHubContext<LeaderboardHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotifyLeaderboardUpdatedAsync(Guid contestId)
        {
            await _hubContext.Clients
                .Group($"leaderboard_{contestId}")
                .SendAsync("LeaderboardUpdated", new
                {
                    ContestId = contestId,
                    Message = "Leaderboard has been updated",
                    Timestamp = DateTime.UtcNow
                });
        }
    }
}
