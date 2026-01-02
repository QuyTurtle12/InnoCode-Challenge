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
using Api.IntegrationTests.Infrastructure;

namespace Api.IntegrationTests.Contests
{
    public class RankCutoffApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RankCutoffApiTests(ApiFactory factory)
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

        private static string BuildStudentToken(Guid userId, string email)
        {
            return JwtTestHelper.BuildToken(
                sub: userId.ToString(),
                email: email,
                issuer: "Capstone",
                audience: "Capstone",
                key: "InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge",
                typ: RoleConstants.Student,
                expiresMinutes: 60);
        }

        private sealed record Seed(
            Guid Round1Id,
            Guid Round2Id,
            Guid ContestId,
            Guid TeamTopId,
            Guid TeamLowId,
            Guid StudentTopId,
            Guid StudentLowId,
            string StudentTopEmail,
            string StudentTopPassword,
            string StudentLowEmail,
            string StudentLowPassword);

        private Seed SeedCutoffScenario()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var orgEmail = $"org{Guid.NewGuid():N}@test.com";
            const string orgPwd = "P@ssword123!";
            var orgUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Org",
                Email = orgEmail,
                PasswordHash = PasswordHasher.Hash(orgPwd),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var mentor = new Mentor
            {
                MentorId = Guid.NewGuid(),
                UserId = orgUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var contestId = Guid.NewGuid();
            var contest = new Contest
            {
                ContestId = contestId,
                Name = "Cutoff Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/img.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                Start = now.AddHours(-2),
                End = now.AddDays(10),
                CreatedAt = now,
                CreatedBy = orgUser.UserId.ToString()
            };

            var round1Id = Guid.NewGuid();
            var round1 = new Round
            {
                RoundId = round1Id,
                ContestId = contestId,
                Name = "Round 1",
                Start = now.AddHours(-2),
                End = now.AddHours(-1),
                Status = RoundStatusEnum.Closed.ToString(),
                IsRetakeRound = false
            };

            var round2Id = Guid.NewGuid();
            var round2 = new Round
            {
                RoundId = round2Id,
                ContestId = contestId,
                Name = "Round 2",
                Start = now.AddHours(-0.5),
                End = now.AddDays(1),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problem1 = new Problem
            {
                ProblemId = Guid.NewGuid(),
                RoundId = round1Id,
                Language = "python",
                Type = ProblemTypeEnum.AutoEvaluation.ToString(),
                CreatedAt = now
            };

            var problem2 = new Problem
            {
                ProblemId = Guid.NewGuid(),
                RoundId = round2Id,
                Language = "python",
                Type = ProblemTypeEnum.Manual.ToString(),
                CreatedAt = now
            };

            var studentTopEmail = $"sTop{Guid.NewGuid():N}@test.com";
            var studentLowEmail = $"sLow{Guid.NewGuid():N}@test.com";
            const string pwd = "P@ssword123!";

            var studentTopUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Top Student",
                Email = studentTopEmail,
                PasswordHash = PasswordHasher.Hash(pwd),
                Role = RoleConstants.Student,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var studentLowUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Low Student",
                Email = studentLowEmail,
                PasswordHash = PasswordHasher.Hash(pwd),
                Role = RoleConstants.Student,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var studentTop = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = studentTopUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var studentLow = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = studentLowUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var teamTopId = Guid.NewGuid();
            var teamLowId = Guid.NewGuid();

            var teamTop = new Team
            {
                TeamId = teamTopId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "TeamTop",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var teamLow = new Team
            {
                TeamId = teamLowId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "TeamLow",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var tmTop = new TeamMember { TeamId = teamTopId, StudentId = studentTop.StudentId, MemberRole = "leader", JoinedAt = now };
            var tmLow = new TeamMember { TeamId = teamLowId, StudentId = studentLow.StudentId, MemberRole = "leader", JoinedAt = now };

            var subTop = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = teamTopId,
                ProblemId = problem1.ProblemId,
                SubmittedByStudentId = studentTop.StudentId,
                Status = SubmissionStatusEnum.Finished.ToString(),
                Score = 100,
                CreatedAt = now.AddMinutes(-50)
            };

            var subLow = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = teamLowId,
                ProblemId = problem1.ProblemId,
                SubmittedByStudentId = studentLow.StudentId,
                Status = SubmissionStatusEnum.Finished.ToString(),
                Score = 10,
                CreatedAt = now.AddMinutes(-40)
            };

            db.Users.AddRange(orgUser, studentTopUser, studentLowUser);
            db.Students.AddRange(studentTop, studentLow);
            db.Mentors.Add(mentor);
            db.Contests.Add(contest);
            db.Rounds.AddRange(round1, round2);
            db.Problems.AddRange(problem1, problem2);
            db.Teams.AddRange(teamTop, teamLow);
            db.TeamMembers.AddRange(tmTop, tmLow);
            db.Submissions.AddRange(subTop, subLow);
            db.Configs.Add(new Config
            {
                Key = ConfigKeys.RoundRankCutoff(round2Id),
                Value = "1",
                Scope = "contest",
                UpdatedAt = now
            });
            db.SaveChanges();

            return new Seed(
                Round1Id: round1Id,
                Round2Id: round2Id,
                ContestId: contestId,
                TeamTopId: teamTopId,
                TeamLowId: teamLowId,
                StudentTopId: studentTop.StudentId,
                StudentLowId: studentLow.StudentId,
                StudentTopEmail: studentTopEmail,
                StudentTopPassword: pwd,
                StudentLowEmail: studentLowEmail,
                StudentLowPassword: pwd);
        }

        [Fact]
        public async Task TeamOutsideCutoff_ShouldBeRejectedInNextRound()
        {
            var seed = SeedCutoffScenario();

            var tokenLow = BuildStudentToken(seed.StudentLowId, seed.StudentLowEmail);
            var form = new MultipartFormDataContent();
            var bytes = System.Text.Encoding.UTF8.GetBytes("dummy content");
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(fileContent, "file", "dummy.zip");

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.Round2Id:D}/manual-test/submissions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenLow);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
