using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Contests
{
    public class RoundAppealReviewAutoDenyApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundAppealReviewAutoDenyApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid RoundId,
            Guid AppealId,
            string OrganizerEmail,
            string OrganizerPassword);

        private SeedData SeedRoundWithPendingAppeal(DateTime now)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var organizerEmail = $"organizer{Guid.NewGuid():N}@test.com";
            const string organizerPassword = "P@ssword123!";
            var organizerUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Organizer",
                Email = organizerEmail,
                PasswordHash = PasswordHasher.Hash(organizerPassword),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var mentorUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Mentor",
                Email = $"mentor{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Mentor,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var ownerUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Student",
                Email = $"student{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Student,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var mentor = new Mentor
            {
                MentorId = Guid.NewGuid(),
                UserId = mentorUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var contest = new Contest
            {
                ContestId = Guid.NewGuid(),
                Name = $"Appeal Auto Deny {Guid.NewGuid():N}",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-10),
                End = now.AddHours(10),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var round = new Round
            {
                RoundId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                Name = "Round Appeal",
                Start = now.AddHours(-6),
                End = now.AddHours(-5),
                Status = RoundStatusEnum.Closed.ToString(),
                IsRetakeRound = false
            };

            var team = new Team
            {
                TeamId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "Team Appeal",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var appeal = new Appeal
            {
                AppealId = Guid.NewGuid(),
                TeamId = team.TeamId,
                TargetType = TargetTypes.Round,
                TargetId = round.RoundId,
                OwnerId = ownerUser.UserId,
                State = AppealStateEnum.Opened.ToString(),
                Decision = AppealDecisionEnum.Pending.ToString(),
                Reason = "Need review",
                CreatedAt = now
            };

            db.Users.AddRange(organizerUser, mentorUser, ownerUser);
            db.Mentors.Add(mentor);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Teams.Add(team);
            db.Appeals.Add(appeal);
            db.Configs.AddRange(
                new Config
                {
                    Key = ConfigKeys.RoundAppealSubmitDeadlineUtc(round.RoundId),
                    Value = now.AddHours(-2).ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                },
                new Config
                {
                    Key = ConfigKeys.RoundAppealReviewDeadlineUtc(round.RoundId),
                    Value = now.AddHours(2).ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                });
            db.SaveChanges();

            return new SeedData(
                RoundId: round.RoundId,
                AppealId: appeal.AppealId,
                OrganizerEmail: organizerEmail,
                OrganizerPassword: organizerPassword);
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
        public async Task AppealReview_End_ShouldAutoDenyPendingAppeals()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRoundWithPendingAppeal(now);
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/appeal-review-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var appeal = await db.Appeals.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppealId == seed.AppealId);

            appeal.Should().NotBeNull();
            appeal!.State.Should().Be(AppealStateEnum.Closed.ToString());
            appeal.Decision.Should().Be(AppealDecisionEnum.Rejected.ToString());
            appeal.DecisionReason.Should().NotBeNullOrWhiteSpace();
            appeal.DecisionReason.Should().Contain("Auto-denied");
        }
    }
}
