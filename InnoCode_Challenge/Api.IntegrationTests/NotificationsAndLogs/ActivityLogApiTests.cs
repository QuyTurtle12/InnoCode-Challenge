using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.ActivityLogDTOs;
using Repository.DTOs.AuthDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Xunit;

namespace Api.IntegrationTests.NotificationsAndLogs
{
    public class ActivityLogApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public ActivityLogApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<string> LoginAdminAsync()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = TestSeed.AdminEmail,
                Password = TestSeed.AdminPassword
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<AuthResponseDTO>();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private ActivityLog SeedLog(string targetType)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLowerInvariant());
            var log = new ActivityLog
            {
                LogId = Guid.NewGuid(),
                UserId = admin.UserId,
                Action = ActivityActions.CertificateIssue,
                TargetType = targetType,
                TargetId = Guid.NewGuid().ToString(),
                At = DateTime.UtcNow
            };

            db.ActivityLogs.Add(log);
            db.SaveChanges();

            return log;
        }

        [Fact]
        public async Task Get_WhenPageInvalid_ShouldReturn400_BADREQUEST()
        {
            var token = await LoginAdminAsync();

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/activitylogs?page=0&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task Get_WhenPageSizeInvalid_ShouldReturn400_BADREQUEST()
        {
            var token = await LoginAdminAsync();

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/activitylogs?page=1&pageSize=0");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task Get_WhenTargetTypeCaseDiff_ShouldStillMatch()
        {
            SeedLog("Certificate");
            var token = await LoginAdminAsync();

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/activitylogs?targetType=certificate&page=1&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<List<ActivityLogDTO>>();
            body.Data.Should().NotBeNull();
            body.Data!.Should().ContainSingle(x => x.TargetType == "Certificate");
        }
    }
}
