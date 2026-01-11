using BusinessLogic.Hubs;
using BusinessLogic.IServices.Dashboards;
using Microsoft.AspNetCore.SignalR;

namespace BusinessLogic.Services.Dashboards
{
    public class DashboardNotifierService : IDashboardNotifierService
    {
        private readonly IHubContext<DashboardHub> _hubContext;

        public DashboardNotifierService(IHubContext<DashboardHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotifyContestCreatedAsync()
        {
            await _hubContext.Clients.Group("AdminDashboard")
                .SendAsync("ContestCreated", new
                {
                    Message = "A new contest has been created",
                    Timestamp = DateTime.UtcNow
                });
        }

        public async Task NotifyContestStatusChangedAsync()
        {
            await _hubContext.Clients.Group("AdminDashboard")
                .SendAsync("ContestStatusChanged", new
                {
                    Message = "Contest status has been updated",
                    Timestamp = DateTime.UtcNow
                });
        }

        public async Task NotifyTeamRegisteredAsync()
        {
            await _hubContext.Clients.Group("AdminDashboard")
                .SendAsync("TeamRegistered", new
                {
                    Message = "A new team has registered",
                    Timestamp = DateTime.UtcNow
                });
        }

        public async Task NotifyCertificateIssuedAsync()
        {
            await _hubContext.Clients.Group("AdminDashboard")
                .SendAsync("CertificateIssued", new
                {
                    Message = "A certificate has been issued",
                    Timestamp = DateTime.UtcNow
                });
        }

        public async Task NotifyMentorDashboardUpdatedAsync(Guid mentorId)
        {
            await _hubContext.Clients.Group($"MentorDashboard_{mentorId}")
                .SendAsync("MentorDashboardUpdated", new
                {
                    MentorId = mentorId,
                    Message = "Your mentor dashboard has been updated",
                    Timestamp = DateTime.UtcNow
                });
        }
    }
}
