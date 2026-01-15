using BusinessLogic.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Api.IntegrationTests.NotificationsAndLogs
{
    public class ActivityLogsHubTests
    {
        [Fact]
        public void ActivityLogsHub_ShouldRequireStaffOrAdmin()
        {
            var attr = typeof(ActivityLogsHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .FirstOrDefault();

            attr.Should().NotBeNull();
            attr!.Policy.Should().Be("RequireStaffOrAdmin");
        }
    }
}
