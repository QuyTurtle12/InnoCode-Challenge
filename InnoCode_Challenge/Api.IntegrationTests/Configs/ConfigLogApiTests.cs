using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.ConfigDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Xunit;

namespace Api.IntegrationTests.Configs
{
    public class ConfigLogApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public ConfigLogApiTests(ApiFactory factory)
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

        [Fact]
        public async Task CreateConfig_ShouldWriteActivityLog()
        {
            var token = await LoginAdminAsync();

            var key = $"test:config:{Guid.NewGuid():N}";
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/configs");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateConfigDTO
            {
                Key = key,
                Value = "true",
                Scope = "global"
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLowerInvariant());
            db.ActivityLogs.Any(l => l.UserId == admin.UserId
                                     && l.Action == ActivityActions.AdminConfigChange
                                     && l.TargetType == TargetTypes.SystemConfig
                                     && l.TargetId == key).Should().BeTrue();
        }
    }
}
