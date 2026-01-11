using BusinessLogic.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.IServices.Dashboards
{
    public interface IDashboardNotifierService
    {
        // Admin Dashboard Notifications
        Task NotifyContestCreatedAsync();
        Task NotifyContestStatusChangedAsync();
        Task NotifyTeamRegisteredAsync();
        Task NotifyCertificateIssuedAsync();

        // Mentor Dashboard Notifications
        Task NotifyMentorDashboardUpdatedAsync(Guid mentorId);
    }
}
