using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
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
    public class RoundTimeTravelActivityLogApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundTimeTravelActivityLogApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid RoundId,
            Guid OrganizerUserId,
            string OrganizerEmail,
            string OrganizerPassword);

        private SeedData SeedRound(
            DateTime now,
            ProblemTypeEnum problemType,
            DateTime roundEnd,
            DateTime? appealSubmitDeadline = null,
            DateTime? appealReviewDeadline = null)
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

            var contest = new Contest
            {
                ContestId = Guid.NewGuid(),
                Name = $"Time Travel Log Contest {Guid.NewGuid():N}",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-12),
                End = now.AddHours(12),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var round = new Round
            {
                RoundId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                Name = "Round TT Log",
                Start = roundEnd.AddHours(-2),
                End = roundEnd,
                Status = roundEnd <= now ? RoundStatusEnum.Closed.ToString() : RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problem = new Problem
            {
                ProblemId = Guid.NewGuid(),
                RoundId = round.RoundId,
                Language = "markdown",
                Type = problemType.ToString(),
                CreatedAt = now
            };

            db.Users.Add(organizerUser);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);

            if (appealSubmitDeadline.HasValue)
            {
                db.Configs.Add(new Config
                {
                    Key = ConfigKeys.RoundAppealSubmitDeadlineUtc(round.RoundId),
                    Value = appealSubmitDeadline.Value.ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                });
            }

            if (appealReviewDeadline.HasValue)
            {
                db.Configs.Add(new Config
                {
                    Key = ConfigKeys.RoundAppealReviewDeadlineUtc(round.RoundId),
                    Value = appealReviewDeadline.Value.ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                });
            }

            db.SaveChanges();

            return new SeedData(
                RoundId: round.RoundId,
                OrganizerUserId: organizerUser.UserId,
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
        public async Task AppealSubmit_End_ShouldWriteActivityLog()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(now, ProblemTypeEnum.AutoEvaluation, roundEnd: now.AddHours(-1));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/appeal-submit-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.RoundAppealSubmitEnd
                                     && l.TargetType == TargetTypes.Round
                                     && l.TargetId == seed.RoundId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task AppealReview_End_ShouldWriteActivityLog()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.AutoEvaluation,
                roundEnd: now.AddDays(-3),
                appealSubmitDeadline: now.AddHours(-1),
                appealReviewDeadline: now.AddHours(2));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/appeal-review-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.RoundAppealReviewEnd
                                     && l.TargetType == TargetTypes.Round
                                     && l.TargetId == seed.RoundId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task JudgeDeadline_End_ShouldWriteActivityLog()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.Manual,
                roundEnd: now.AddHours(-1),
                appealSubmitDeadline: now.AddHours(1),
                appealReviewDeadline: now.AddHours(3));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.RoundJudgeDeadlineEnd
                                     && l.TargetType == TargetTypes.Round
                                     && l.TargetId == seed.RoundId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task Finalize_ShouldWriteActivityLog()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.AutoEvaluation,
                roundEnd: now.AddDays(-5),
                appealSubmitDeadline: now.AddDays(-4),
                appealReviewDeadline: now.AddDays(-3));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/finalize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.RoundFinalize
                                     && l.TargetType == TargetTypes.Round
                                     && l.TargetId == seed.RoundId.ToString()).Should().BeTrue();
        }
    }
}
