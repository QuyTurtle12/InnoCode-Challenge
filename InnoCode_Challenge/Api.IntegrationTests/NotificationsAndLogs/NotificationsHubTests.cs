using BusinessLogic.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Api.IntegrationTests.NotificationsAndLogs
{
    public class NotificationsHubTests
    {
        [Fact]
        public void NotificationsHub_ShouldRequireAuthorization()
        {
            var attr = typeof(NotificationsHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .FirstOrDefault();

            attr.Should().NotBeNull();
        }
    }
}
