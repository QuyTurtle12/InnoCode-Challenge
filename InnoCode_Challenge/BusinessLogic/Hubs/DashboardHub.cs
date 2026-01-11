using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Hubs
{
    public class DashboardHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        public async Task RequestDashboardRefresh()
        {
            await Clients.Caller.SendAsync("DashboardRefreshRequested");
        }

        public async Task JoinAdminDashboard()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "AdminDashboard");
        }

        public async Task LeaveAdminDashboard()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "AdminDashboard");
        }

        public async Task JoinMentorDashboard(string mentorId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"MentorDashboard_{mentorId}");
        }

        public async Task LeaveMentorDashboard(string mentorId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"MentorDashboard_{mentorId}");
        }
    }
}
