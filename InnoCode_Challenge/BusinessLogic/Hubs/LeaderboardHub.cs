using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Hubs
{
    public class LeaderboardHub : Hub
    {
        public async Task JoinLeaderboardGroup(string contestId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"leaderboard_{contestId}");
        }

        public async Task LeaveLeaderboardGroup(string contestId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"leaderboard_{contestId}");
        }
    }
}
