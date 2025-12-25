using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.NotificationDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.NotificationsAndLogs
{
    public class NotificationApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public NotificationApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record UserSeed(Guid UserId, string Email, string Password);

        private sealed class NotificationPage
        {
            public List<GetNotificationDTO> Items { get; set; } = new();
            public int PageNumber { get; set; }
            public int TotalPages { get; set; }
            public int TotalCount { get; set; }
            public int PageSize { get; set; }
            public bool HasPreviousPage { get; set; }
            public bool HasNextPage { get; set; }
        }

        private UserSeed SeedUser()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var email = $"notif{Guid.NewGuid():N}@test.com";
            const string password = "P@ssword123!";

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Notification User",
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = RoleConstants.Student,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Users.Add(user);
            db.SaveChanges();

            return new UserSeed(user.UserId, email, password);
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
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private Notification SeedNotification(Guid userId, bool isRead, DateTime sentAt)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var entity = new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationTypes.SubmissionStatusChanged,
                Channel = NotificationChannels.InApp,
                Payload = "{\"message\":\"test\"}",
                SentAt = sentAt,
                IsRead = isRead,
                ReadAt = isRead ? sentAt.AddMinutes(1) : null
            };

            db.Notifications.Add(entity);
            db.SaveChanges();

            return entity;
        }

        [Fact]
        public async Task GetMyNotifications_WhenPageInvalid_ShouldReturn400_BADREQUEST()
        {
            var seed = SeedUser();
            var token = await LoginAsync(seed.Email, seed.Password);

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications?pageNumber=0&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task GetMyNotifications_ShouldOrderUnreadFirstThenSentAtDesc()
        {
            var seed = SeedUser();
            var now = DateTime.UtcNow;

            var unreadOld = SeedNotification(seed.UserId, false, now.AddMinutes(-10));
            var unreadNew = SeedNotification(seed.UserId, false, now.AddMinutes(-1));
            var readNewest = SeedNotification(seed.UserId, true, now);

            var token = await LoginAsync(seed.Email, seed.Password);

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications?pageNumber=1&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<NotificationPage>();
            body.Data.Should().NotBeNull();

            var items = body.Data!.Items.ToList();
            items.Should().HaveCount(3);

            items[0].NotificationId.Should().Be(unreadNew.NotificationId);
            items[1].NotificationId.Should().Be(unreadOld.NotificationId);
            items[2].NotificationId.Should().Be(readNewest.NotificationId);
        }

        [Fact]
        public async Task MarkAsRead_ShouldBeIdempotent()
        {
            var seed = SeedUser();
            var notification = SeedNotification(seed.UserId, false, DateTime.UtcNow.AddMinutes(-5));
            var token = await LoginAsync(seed.Email, seed.Password);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/notifications/{notification.NotificationId:D}/read");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<MarkReadResultDTO>();
            body.Data!.UpdatedCount.Should().Be(1);

            var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/notifications/{notification.NotificationId:D}/read");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.OK);

            var body2 = await res2.ReadOkAsync<MarkReadResultDTO>();
            body2.Data!.UpdatedCount.Should().Be(0);
        }

        [Fact]
        public async Task MarkAllAsRead_ShouldIgnoreFutureNotifications()
        {
            var seed = SeedUser();
            var past = SeedNotification(seed.UserId, false, DateTime.UtcNow.AddMinutes(-10));
            var future = SeedNotification(seed.UserId, false, DateTime.UtcNow.AddMinutes(10));

            var token = await LoginAsync(seed.Email, seed.Password);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/notifications/read-all");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<MarkReadResultDTO>();
            body.Data!.UpdatedCount.Should().Be(1);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var pastRow = db.Notifications.First(n => n.NotificationId == past.NotificationId);
            var futureRow = db.Notifications.First(n => n.NotificationId == future.NotificationId);

            pastRow.IsRead.Should().BeTrue();
            futureRow.IsRead.Should().BeFalse();
        }

        [Fact]
        public async Task GetUnreadCount_ShouldMatchCurrentState()
        {
            var seed = SeedUser();
            var unread1 = SeedNotification(seed.UserId, false, DateTime.UtcNow.AddMinutes(-5));
            SeedNotification(seed.UserId, false, DateTime.UtcNow.AddMinutes(-2));
            SeedNotification(seed.UserId, true, DateTime.UtcNow.AddMinutes(-1));

            var token = await LoginAsync(seed.Email, seed.Password);

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/unread-count");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<UnreadCountDTO>();
            body.Data!.Count.Should().Be(2);

            var mark = new HttpRequestMessage(HttpMethod.Post, $"/api/notifications/{unread1.NotificationId:D}/read");
            mark.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await _client.SendAsync(mark);

            var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/unread-count");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.OK);

            var body2 = await res2.ReadOkAsync<UnreadCountDTO>();
            body2.Data!.Count.Should().Be(1);
        }
    }
}
