using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Hubs
{
    [Authorize]
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
