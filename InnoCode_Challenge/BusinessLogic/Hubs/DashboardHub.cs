using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Hubs
{
    public class DashboardHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "AdminDashboard");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "AdminDashboard");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task RequestDashboardRefresh()
        {
            await Clients.Caller.SendAsync("DashboardRefreshRequested");
        }
    }
}
