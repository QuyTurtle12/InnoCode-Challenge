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
    public class RoundTimelineJudgeDeadlineApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundTimelineJudgeDeadlineApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid RoundId,
            string OrganizerEmail,
            string OrganizerPassword);

        private SeedData SeedManualRoundWithSubmission()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

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

            var judgeUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Judge",
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
                Fullname = "Mentor",
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
                Name = "Timeline Judge Deadline Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-10),
                End = now.AddHours(2),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var round = new Round
            {
                RoundId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                Name = "Manual Round",
                Start = now.AddHours(-5),
                End = now.AddHours(-1),
                Status = RoundStatusEnum.Closed.ToString(),
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
                CreatedAt = now.AddHours(-2)
            };

            db.Users.AddRange(organizerUser, judgeUser, mentorUser, studentUser);
            db.Mentors.Add(mentor);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.Submissions.Add(submission);
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
        public async Task FastForwardJudgeDeadline_ShouldUpdateTimeline()
        {
            var seed = SeedManualRoundWithSubmission();

            var beforeRes = await _client.GetAsync($"/api/rounds/{seed.RoundId:D}/timeline");
            beforeRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var before = await beforeRes.ReadOkAsync<RoundTimelineDTO>();
            before.Data!.JudgeDeadline.Should().NotBeNull();
            DateTime beforeDeadline = before.Data!.JudgeDeadline!.Value;

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var fastForwardRes = await _client.SendAsync(req);
            fastForwardRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var afterRes = await _client.GetAsync($"/api/rounds/{seed.RoundId:D}/timeline");
            afterRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var after = await afterRes.ReadOkAsync<RoundTimelineDTO>();
            after.Data!.JudgeDeadline.Should().NotBeNull();
            DateTime afterDeadline = after.Data!.JudgeDeadline!.Value;

            afterDeadline.Should().BeBefore(beforeDeadline);
            afterDeadline.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
        }
    }
}
