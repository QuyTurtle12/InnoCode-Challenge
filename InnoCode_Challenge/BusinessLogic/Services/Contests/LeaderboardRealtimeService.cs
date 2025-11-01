using BusinessLogic.Hubs;
using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.SignalR;
using Repository.DTOs.LeaderboardEntryDTOs;

namespace BusinessLogic.Services.Contests
{
    public class LeaderboardRealtimeService : ILeaderboardRealtimeService
    {
        private readonly IHubContext<LeaderboardHub> _hubContext;

        public LeaderboardRealtimeService(IHubContext<LeaderboardHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task BroadcastLeaderboardUpdateAsync(Guid contestId, IList<TeamInfo> leaderboard)
        {
            await _hubContext.Clients
                .Group($"leaderboard_{contestId}")
                .SendAsync("LeaderboardUpdated", new
                {
                    ContestId = contestId,
                    Leaderboard = leaderboard,
                    Timestamp = DateTime.UtcNow
                });
        }
    }
}
