using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.RubricDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Submissions
{
    public class JudgeDeadlineApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public JudgeDeadlineApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid SubmissionId,
            Guid JudgeUserId,
            string JudgeEmail,
            string JudgePassword,
            Guid RubricId);

        private SeedData SeedManualSubmission(DateTime? roundEndOverride = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var now = DateTime.UtcNow;

            var judgeEmail = $"judge{Guid.NewGuid():N}@test.com";
            const string judgePassword = "P@ssword123!";
            var judgeUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Judge Tester",
                Email = judgeEmail,
                PasswordHash = PasswordHasher.Hash(judgePassword),
                Role = RoleConstants.Judge,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var studentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Student Tester",
                Email = $"student{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Student,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var student = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = studentUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var contest = new Contest
            {
                ContestId = Guid.NewGuid(),
                Name = "Judge Deadline Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/img.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-2),
                End = now.AddHours(5),
                CreatedBy = judgeUser.UserId.ToString()
            };

            var round = new Round
            {
                RoundId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                Name = "Manual Round",
                Start = now.AddHours(-1),
                End = roundEndOverride ?? now.AddHours(2),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problem = new Problem
            {
                ProblemId = Guid.NewGuid(),
                RoundId = round.RoundId,
                Language = "markdown",
                Type = ProblemTypeEnum.Manual.ToString(),
                CreatedAt = now
            };

            var team = new Team
            {
                TeamId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = Guid.NewGuid(),
                Name = "Team Manual",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var submission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = team.TeamId,
                ProblemId = problem.ProblemId,
                SubmittedByStudentId = student.StudentId,
                JudgedBy = judgeUser.UserId.ToString(),
                Status = SubmissionStatusEnum.Pending.ToString(),
                Score = 0,
                CreatedAt = now
            };

            var rubric = new TestCase
            {
                TestCaseId = Guid.NewGuid(),
                ProblemId = problem.ProblemId,
                Description = "Content quality",
                Type = TestCaseTypeEnum.Manual.ToString(),
                Weight = 10,
                OrderIndex = 1
            };

            db.Users.AddRange(judgeUser, studentUser);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.Submissions.Add(submission);
            db.TestCases.Add(rubric);
            db.SaveChanges();

            return new SeedData(
                SubmissionId: submission.SubmissionId,
                JudgeUserId: judgeUser.UserId,
                JudgeEmail: judgeEmail,
                JudgePassword: judgePassword,
                RubricId: rubric.TestCaseId);
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

        private static SubmitRubricScoreDTO BuildRubricPayload(Guid rubricId, double score)
        {
            return new SubmitRubricScoreDTO
            {
                CriterionScores =
                [
                    new RubricCriterionScoreDTO
                    {
                        RubricId = rubricId,
                        Score = score,
                        Note = "Looks good"
                    }
                ]
            };
        }

        [Fact]
        public async Task SubmitRubric_AfterDeadline_ShouldReturn403()
        {
            var seed = SeedManualSubmission(DateTime.UtcNow.AddDays(-2));

            var token = await LoginAsync(seed.JudgeEmail, seed.JudgePassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/submissions/{seed.SubmissionId:D}/rubric-evaluation");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildRubricPayload(seed.RubricId, 8));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("JUDGE_DEADLINE_PASSED");
        }

        [Fact]
        public async Task SubmitRubric_BeforeDeadline_ShouldSucceed()
        {
            var seed = SeedManualSubmission();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                db.Configs.Add(new Config
                {
                    Key = ConfigKeys.JudgeSubmissionDeadline(seed.JudgeUserId, seed.SubmissionId),
                    Value = DateTime.UtcNow.AddMinutes(30).ToString("o"),
                    Scope = "submission",
                    UpdatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var token = await LoginAsync(seed.JudgeEmail, seed.JudgePassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/submissions/{seed.SubmissionId:D}/rubric-evaluation");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildRubricPayload(seed.RubricId, 7.5));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<RubricEvaluationResultDTO>();
            body.Data!.TotalScore.Should().Be(7.5);
        }

        [Fact]
        public async Task Rescore_AfterDeadline_ShouldReturn403()
        {
            var seed = SeedManualSubmission(DateTime.UtcNow.AddDays(-2));

            var token = await LoginAsync(seed.JudgeEmail, seed.JudgePassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/submissions/{seed.SubmissionId:D}/rubric-evaluation");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildRubricPayload(seed.RubricId, 6));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("JUDGE_DEADLINE_PASSED");
        }

        [Fact]
        public async Task Rescore_BeforeDeadline_ShouldSucceed()
        {
            var seed = SeedManualSubmission();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                db.Configs.Add(new Config
                {
                    Key = ConfigKeys.JudgeSubmissionDeadline(seed.JudgeUserId, seed.SubmissionId),
                    Value = DateTime.UtcNow.AddMinutes(60).ToString("o"),
                    Scope = "submission",
                    UpdatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var token = await LoginAsync(seed.JudgeEmail, seed.JudgePassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/submissions/{seed.SubmissionId:D}/rubric-evaluation");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildRubricPayload(seed.RubricId, 9));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<RubricEvaluationResultDTO>();
            body.Data!.TotalScore.Should().Be(9);
        }
    }
}
