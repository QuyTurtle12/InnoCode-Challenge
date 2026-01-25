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
    public class RoundDistributionActivityLogApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundDistributionActivityLogApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid RoundId,
            Guid SubmissionId,
            Guid OrganizerUserId,
            string OrganizerEmail,
            string OrganizerPassword);

        private SeedData SeedManualRoundWithPendingSubmission()
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

            var judgeUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Judge",
                Email = $"judge{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Judge,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var mentorUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Mentor",
                Email = $"mentor{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Mentor,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var studentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Student",
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
                Name = "Distribution Log Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-3),
                End = now.AddHours(3),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var round = new Round
            {
                RoundId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                Name = "Manual Round",
                Start = now.AddHours(-2),
                End = now.AddHours(2),
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
                MentorId = mentor.MentorId,
                Name = "Team A",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var teamMember = new TeamMember
            {
                TeamId = team.TeamId,
                StudentId = student.StudentId,
                MemberRole = "leader",
                JoinedAt = now
            };

            var submission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = team.TeamId,
                ProblemId = problem.ProblemId,
                SubmittedByStudentId = student.StudentId,
                JudgedBy = null,
                Status = SubmissionStatusEnum.Pending.ToString(),
                Score = 0,
                CreatedAt = now
            };

            var judgeConfig = new Config
            {
                Key = ConfigKeys.ContestJudge(contest.ContestId, judgeUser.UserId),
                Value = "judge",
                Scope = "contest",
                UpdatedAt = now
            };

            db.Users.AddRange(organizerUser, judgeUser, mentorUser, studentUser);
            db.Mentors.Add(mentor);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.Submissions.Add(submission);
            db.Configs.Add(judgeConfig);
            db.SaveChanges();

            return new SeedData(
                RoundId: round.RoundId,
                SubmissionId: submission.SubmissionId,
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
        public async Task EndNow_ShouldWriteAssignJudgeActivityLog()
        {
            var seed = SeedManualRoundWithPendingSubmission();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/rounds/{seed.RoundId:D}/end-now");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.SubmissionAssignJudge
                                     && l.TargetType == TargetTypes.Submission
                                     && l.TargetId == seed.SubmissionId.ToString()).Should().BeTrue();
        }
    }
}
