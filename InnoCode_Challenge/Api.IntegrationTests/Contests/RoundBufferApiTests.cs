using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.ContestDTOs;
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
    public class RoundBufferApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundBufferApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
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

        private async Task<(Guid ContestId, string Token)> CreateContestAsync(string organizerEmail, string organizerPassword)
        {
            var now = DateTime.UtcNow;
            var contestName = $"Buffer Contest {Guid.NewGuid():N}";
            var form = new MultipartFormDataContent
            {
                { new StringContent(now.Year.ToString()), "Year" },
                { new StringContent(contestName), "Name" },
                { new StringContent(now.AddDays(-1).ToString("o")), "Start" },
                { new StringContent(now.AddDays(30).ToString("o")), "End" }
            };

            var token = await LoginAsync(organizerEmail, organizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/contests/advanced");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
            var created = await res.ReadOkAsync<ContestCreatedDTO>();
            return (created.Data!.ContestId, token);
        }

        private (string Email, string Password) SeedOrganizer()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;
            var email = $"org{Guid.NewGuid():N}@test.com";
            const string password = "P@ssword123!";

            var org = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Org",
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Users.Add(org);
            db.SaveChanges();
            return (email, password);
        }

        [Fact]
        public async Task ManualRound_ShouldEnforceBuffer_BeforeCreate()
        {
            var org = SeedOrganizer();
            var (contestId, token) = await CreateContestAsync(org.Email, org.Password);

            // round1 manual
            var now = DateTime.UtcNow;
            var round1 = new MultipartFormDataContent
            {
                { new StringContent("Round1"), "Name" },
                { new StringContent(now.AddDays(0).ToString("o")), "Start" },
                { new StringContent(now.AddDays(1).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            round1.Add(new StringContent("false"), "IsRetakeRound");

            var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req1.Content = round1;
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

            // round2 manual but start earlier than buffer (manual buffer = judge*2 + submit + review = 1*2 +2+1 =5 days)
            var round2 = new MultipartFormDataContent
            {
                { new StringContent("Round2"), "Name" },
                { new StringContent(now.AddDays(2).ToString("o")), "Start" },
                { new StringContent(now.AddDays(3).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            round2.Add(new StringContent("false"), "IsRetakeRound");

            var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2.Content = round2;
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // valid round2 after buffer
            var round2b = new MultipartFormDataContent
            {
                { new StringContent("Round2b"), "Name" },
                { new StringContent(now.AddDays(6).ToString("o")), "Start" },
                { new StringContent(now.AddDays(7).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            round2b.Add(new StringContent("false"), "IsRetakeRound");

            var req2b = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            req2b.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2b.Content = round2b;
            var res2b = await _client.SendAsync(req2b);
            res2b.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        }

        [Fact]
        public async Task AutoRound_ShouldUseSubmitAndReviewBuffer()
        {
            var org = SeedOrganizer();
            var (contestId, token) = await CreateContestAsync(org.Email, org.Password);

            var now = DateTime.UtcNow;
            var round1 = new MultipartFormDataContent
            {
                { new StringContent("Auto1"), "Name" },
                { new StringContent(now.AddDays(0).ToString("o")), "Start" },
                { new StringContent(now.AddDays(1).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.AutoEvaluation.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.AutoEvaluation.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            round1.Add(new StringContent("false"), "IsRetakeRound");

            var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req1.Content = round1;
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

            // Default buffer for auto = submitDays(2) + reviewDays(1) = 3
            var round2 = new MultipartFormDataContent
            {
                { new StringContent("Auto2"), "Name" },
                { new StringContent(now.AddDays(2).ToString("o")), "Start" }, // too early (< 3 days after end)
                { new StringContent(now.AddDays(3).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.AutoEvaluation.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.AutoEvaluation.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            round2.Add(new StringContent("false"), "IsRetakeRound");

            var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2.Content = round2;
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Retake_ShouldFail_WhenInterveningRoundExists()
        {
            var org = SeedOrganizer();
            var (contestId, token) = await CreateContestAsync(org.Email, org.Password);

            var now = DateTime.UtcNow;
            var main = new MultipartFormDataContent
            {
                { new StringContent("Main"), "Name" },
                { new StringContent(now.AddDays(0).ToString("o")), "Start" },
                { new StringContent(now.AddDays(1).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            main.Add(new StringContent("false"), "IsRetakeRound");

            var mainReq = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            mainReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            mainReq.Content = main;
            var mainRes = await _client.SendAsync(mainReq);
            mainRes.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

            // Insert an intervening round
            var mid = new MultipartFormDataContent
            {
                { new StringContent("Mid"), "Name" },
                { new StringContent(now.AddDays(7).ToString("o")), "Start" },
                { new StringContent(now.AddDays(8).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.AutoEvaluation.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.AutoEvaluation.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" }
            };
            mid.Add(new StringContent("false"), "IsRetakeRound");

            var midReq = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            midReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            midReq.Content = mid;
            var midRes = await _client.SendAsync(midReq);
            midRes.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

            // Attempt retake after main, should fail because mid is in between
            Guid mainRoundId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                mainRoundId = db.Rounds
                    .Where(r => r.ContestId == contestId && r.Name == "Main")
                    .Select(r => r.RoundId)
                    .First();
            }

            var retake = new MultipartFormDataContent
            {
                { new StringContent("Retake Main"), "Name" },
                { new StringContent(now.AddDays(9).ToString("o")), "Start" },
                { new StringContent(now.AddDays(10).ToString("o")), "End" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemType" },
                { new StringContent(ProblemTypeEnum.Manual.ToString()), "ProblemConfig.Type" },
                { new StringContent("python"), "ProblemConfig.Language" },
                { new StringContent("true"), "IsRetakeRound" },
                { new StringContent(mainRoundId.ToString()), "MainRoundId" }
            };

            var retakeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{contestId}");
            retakeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            retakeReq.Content = retake;
            var retakeRes = await _client.SendAsync(retakeReq);
            retakeRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
