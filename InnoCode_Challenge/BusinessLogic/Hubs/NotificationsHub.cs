using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BusinessLogic.Hubs
{
    public class NotificationsHub : Hub
    {
        private static string UserGroup(string userId) => $"notifications_{userId}";

        public async Task JoinMyNotificationGroup()
        {
            var userId = Context.UserIdentifier; 
            if (string.IsNullOrWhiteSpace(userId)) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        public async Task LeaveMyNotificationGroup()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrWhiteSpace(userId)) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId));
        }
    }
}
