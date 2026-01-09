using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.RoundDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Contests
{
    public class RoundTimeTravelValidationApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundTimeTravelValidationApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid RoundId,
            string OrganizerEmail,
            string OrganizerPassword);

        private SeedData SeedRound(
            DateTime now,
            ProblemTypeEnum problemType,
            bool isRetake,
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
                Name = $"Time Travel Contest {Guid.NewGuid():N}",
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
                Name = "Round TT",
                Start = roundEnd.AddHours(-2),
                End = roundEnd,
                Status = roundEnd <= now ? RoundStatusEnum.Closed.ToString() : RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = isRetake
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
        public async Task AppealSubmit_End_RetakeRound_ShouldReturn400()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(now, ProblemTypeEnum.Manual, true, roundEnd: now.AddHours(-1));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/appeal-submit-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task AppealReview_End_RetakeRound_ShouldReturn400()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(now, ProblemTypeEnum.Manual, true, roundEnd: now.AddHours(-1));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/appeal-review-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task AppealReview_End_BeforeSubmitDeadline_ShouldReturn409()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.AutoEvaluation,
                false,
                roundEnd: now.AddHours(-3),
                appealSubmitDeadline: now.AddHours(2),
                appealReviewDeadline: now.AddHours(5));

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/appeal-review-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_STATE");
        }

        [Fact]
        public async Task JudgeDeadline_End_NonManualRound_ShouldReturn400()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(now, ProblemTypeEnum.AutoEvaluation, false, roundEnd: now.AddHours(-1));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task JudgeDeadline_End_BetweenSubmitAndReview_ShouldReturn409()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.Manual,
                false,
                roundEnd: now.AddHours(-4),
                appealSubmitDeadline: now.AddHours(-1),
                appealReviewDeadline: now.AddHours(2));

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_STATE");
        }

        [Fact]
        public async Task JudgeDeadline_End_BeforeSubmit_ShouldUpdateJudgeDeadline()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.Manual,
                false,
                roundEnd: now.AddHours(-1),
                appealSubmitDeadline: now.AddHours(2),
                appealReviewDeadline: now.AddHours(5));

            var beforeRes = await _client.GetAsync($"/api/rounds/{seed.RoundId:D}/timeline");
            beforeRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var before = await beforeRes.ReadOkAsync<RoundTimelineDTO>();
            before.Data!.JudgeDeadline.Should().NotBeNull();
            DateTime beforeDeadline = before.Data!.JudgeDeadline!.Value;

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var afterRes = await _client.GetAsync($"/api/rounds/{seed.RoundId:D}/timeline");
            afterRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var after = await afterRes.ReadOkAsync<RoundTimelineDTO>();
            after.Data!.JudgeDeadline.Should().NotBeNull();
            DateTime afterDeadline = after.Data!.JudgeDeadline!.Value;

            afterDeadline.Should().BeBefore(beforeDeadline);
            afterDeadline.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public async Task JudgeDeadline_End_AfterReview_ShouldUpdateJudgeRescoreDeadline()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(
                now,
                ProblemTypeEnum.Manual,
                false,
                roundEnd: now.AddHours(-1),
                appealSubmitDeadline: now.AddHours(-3),
                appealReviewDeadline: now.AddHours(-1));

            var beforeRes = await _client.GetAsync($"/api/rounds/{seed.RoundId:D}/timeline");
            beforeRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var before = await beforeRes.ReadOkAsync<RoundTimelineDTO>();
            before.Data!.JudgeRescoreDeadline.Should().NotBeNull();
            DateTime beforeDeadline = before.Data!.JudgeRescoreDeadline!.Value;

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var afterRes = await _client.GetAsync($"/api/rounds/{seed.RoundId:D}/timeline");
            afterRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var after = await afterRes.ReadOkAsync<RoundTimelineDTO>();
            after.Data!.JudgeRescoreDeadline.Should().NotBeNull();
            DateTime afterDeadline = after.Data!.JudgeRescoreDeadline!.Value;

            afterDeadline.Should().BeBefore(beforeDeadline);
            afterDeadline.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public async Task Finalize_BeforeDeadlines_ShouldReturn409()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRound(now, ProblemTypeEnum.AutoEvaluation, false, roundEnd: now.AddHours(2));
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/finalize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_STATE");
        }
    }
}
