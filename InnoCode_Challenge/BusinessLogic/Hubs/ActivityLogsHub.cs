using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BusinessLogic.Hubs
{
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