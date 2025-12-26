using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Hubs
{
    [Authorize(Policy = "RequireStaffOrAdmin")]
    public class ActivityLogsHub : Hub
    {
        public async Task JoinActivityLogs()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "activity_logs");
        }

        public async Task LeaveActivityLogs()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "activity_logs");
        }
    }
}
