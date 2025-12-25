using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.JudgeInviteDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Contests
{
    public class JudgeInviteApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public JudgeInviteApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record Seed(
            Guid ContestId,
            Guid OrganizerUserId,
            string OrganizerEmail,
            string OrganizerPassword,
            Guid OtherOrganizerUserId,
            string OtherOrganizerEmail,
            string OtherOrganizerPassword,
            Guid JudgeUserId,
            string JudgeEmail);

        private Seed SeedJudgeInvite()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var organizerEmail = $"organizer{Guid.NewGuid():N}@test.com";
            const string organizerPassword = "P@ssword123!";
            var organizerUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Organizer",
                Email = organizerEmail,
                PasswordHash = PasswordHasher.Hash(organizerPassword),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var otherOrganizerEmail = $"organizer{Guid.NewGuid():N}@test.com";
            const string otherOrganizerPassword = "P@ssword123!";
            var otherOrganizer = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Other Organizer",
                Email = otherOrganizerEmail,
                PasswordHash = PasswordHasher.Hash(otherOrganizerPassword),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var judgeEmail = $"judge{Guid.NewGuid():N}@test.com";
            var judgeUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Judge",
                Email = judgeEmail,
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Judge,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var contestId = Guid.NewGuid();
            var contest = new Contest
            {
                ContestId = contestId,
                Name = "Judge Invite Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = organizerUser.UserId.ToString()
            };

            db.Users.AddRange(organizerUser, otherOrganizer, judgeUser);
            db.Contests.Add(contest);
            db.SaveChanges();

            return new Seed(
                ContestId: contestId,
                OrganizerUserId: organizerUser.UserId,
                OrganizerEmail: organizerEmail,
                OrganizerPassword: organizerPassword,
                OtherOrganizerUserId: otherOrganizer.UserId,
                OtherOrganizerEmail: otherOrganizerEmail,
                OtherOrganizerPassword: otherOrganizerPassword,
                JudgeUserId: judgeUser.UserId,
                JudgeEmail: judgeEmail);
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

        [Fact]
        public async Task CreateInvite_ShouldNotifyJudge_AndLog()
        {
            var seed = SeedJudgeInvite();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateJudgeInviteDTO { JudgeUserId = seed.JudgeUserId });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<JudgeInviteDTO>();
            body.Data!.InviteId.Should().NotBe(Guid.Empty);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Notifications.Any(n => n.UserId == seed.JudgeUserId
                                      && n.Type == NotificationTypes.JudgeInvitation).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.JudgeInviteCreated
                                     && l.TargetType == TargetTypes.JudgeInvite
                                     && l.TargetId == body.Data!.InviteId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task ResendInvite_ShouldLog_AndNotifyJudge()
        {
            var seed = SeedJudgeInvite();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateJudgeInviteDTO { JudgeUserId = seed.JudgeUserId });

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await createRes.ReadOkAsync<JudgeInviteDTO>();

            var resendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites/{created.Data!.InviteId:D}/resend");
            resendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resendRes = await _client.SendAsync(resendReq);
            resendRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Notifications.Any(n => n.UserId == seed.JudgeUserId
                                      && n.Type == NotificationTypes.JudgeInvitation).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.JudgeInviteResent
                                     && l.TargetType == TargetTypes.JudgeInvite
                                     && l.TargetId == created.Data!.InviteId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task RevokeInvite_ShouldNotifyJudge_AndLog()
        {
            var seed = SeedJudgeInvite();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateJudgeInviteDTO { JudgeUserId = seed.JudgeUserId });

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await createRes.ReadOkAsync<JudgeInviteDTO>();

            var revokeReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/contests/{seed.ContestId:D}/judge-invites/{created.Data!.InviteId:D}");
            revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var revokeRes = await _client.SendAsync(revokeReq);
            revokeRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var invite = db.JudgeInvites.First(i => i.InviteId == created.Data!.InviteId);
            invite.Status.Should().Be("revoked");

            db.Notifications.Any(n => n.UserId == seed.JudgeUserId
                                      && n.Type == NotificationTypes.JudgeInvitationRevoked).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.JudgeInviteRevoked
                                     && l.TargetType == TargetTypes.JudgeInvite
                                     && l.TargetId == created.Data!.InviteId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task AcceptByCode_ShouldAssignJudge_NotifyInviter_AndLog()
        {
            var seed = SeedJudgeInvite();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateJudgeInviteDTO { JudgeUserId = seed.JudgeUserId });

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await createRes.ReadOkAsync<JudgeInviteDTO>();

            var acceptRes = await _client.PostAsync($"/api/judge-invites/accept?inviteCode={created.Data!.InviteCode}&email={seed.JudgeEmail}", null);
            acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var invite = db.JudgeInvites.First(i => i.InviteId == created.Data!.InviteId);
            invite.Status.Should().Be("accepted");

            var configKey = ConfigKeys.ContestJudge(seed.ContestId, seed.JudgeUserId);
            db.Configs.Any(c => c.Key == configKey && c.DeletedAt == null).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.JudgeUserId
                                     && l.Action == ActivityActions.JudgeInviteAccepted
                                     && l.TargetType == TargetTypes.JudgeInvite
                                     && l.TargetId == invite.InviteId.ToString()).Should().BeTrue();

            db.Notifications.Any(n => n.UserId == seed.OrganizerUserId
                                      && n.Type == NotificationTypes.JudgeInvitationAccepted).Should().BeTrue();
        }

        [Fact]
        public async Task DeclineByCode_ShouldNotifyInviter_AndLog()
        {
            var seed = SeedJudgeInvite();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateJudgeInviteDTO { JudgeUserId = seed.JudgeUserId });

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await createRes.ReadOkAsync<JudgeInviteDTO>();

            var declineRes = await _client.PostAsync($"/api/judge-invites/decline?inviteCode={created.Data!.InviteCode}&email={seed.JudgeEmail}", null);
            declineRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var invite = db.JudgeInvites.First(i => i.InviteId == created.Data!.InviteId);
            invite.Status.Should().Be("declined");

            db.ActivityLogs.Any(l => l.UserId == seed.JudgeUserId
                                     && l.Action == ActivityActions.JudgeInviteDeclined
                                     && l.TargetType == TargetTypes.JudgeInvite
                                     && l.TargetId == invite.InviteId.ToString()).Should().BeTrue();

            db.Notifications.Any(n => n.UserId == seed.OrganizerUserId
                                      && n.Type == NotificationTypes.JudgeInvitationDenied).Should().BeTrue();
        }

        [Fact]
        public async Task CreateInvite_WhenOrganizerNotOwner_ShouldReturn403()
        {
            var seed = SeedJudgeInvite();
            var token = await LoginAsync(seed.OtherOrganizerEmail, seed.OtherOrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/contests/{seed.ContestId:D}/judge-invites");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateJudgeInviteDTO { JudgeUserId = seed.JudgeUserId });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
