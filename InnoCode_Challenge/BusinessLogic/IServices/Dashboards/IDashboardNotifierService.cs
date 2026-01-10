using BusinessLogic.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.IServices.Dashboards
{
    public interface IDashboardNotifierService
    {
        Task NotifyContestCreatedAsync();
        Task NotifyContestStatusChangedAsync();
        Task NotifyTeamRegisteredAsync();
        Task NotifyCertificateIssuedAsync();
    }
}
