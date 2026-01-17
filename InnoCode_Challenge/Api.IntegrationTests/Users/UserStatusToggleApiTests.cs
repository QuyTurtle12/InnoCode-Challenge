using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Users
{
    public class UserStatusToggleApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public UserStatusToggleApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private static Guid GetAdminUserId(ApiFactory factory)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            return db.Users
                .Where(u => u.DeletedAt == null && u.Email == TestSeed.AdminEmail.ToLowerInvariant())
                .Select(u => u.UserId)
                .First();
        }

        private async Task<string> LoginAsync(string email, string password)
        {
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = email,
                Password = password
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<AuthResponseDTO>();
            return body.Data!.Token!;
        }

        [Fact]
        public async Task ToggleStatus_Self_ShouldReturn403()
        {
            Guid adminId = GetAdminUserId(_factory);
            var token = await LoginAsync(TestSeed.AdminEmail, TestSeed.AdminPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{adminId:D}/toggle-status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("FORBIDDEN");
        }
    }
}
