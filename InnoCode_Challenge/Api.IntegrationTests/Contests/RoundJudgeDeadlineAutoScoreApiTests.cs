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
    public class RoundJudgeDeadlineAutoScoreApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public RoundJudgeDeadlineAutoScoreApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid RoundId,
            Guid SubmissionId,
            string OrganizerEmail,
            string OrganizerPassword);

        private SeedData SeedRoundWithPendingSubmission(DateTime now)
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
                Name = $"Judge Deadline Contest {Guid.NewGuid():N}",
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
                Name = "Manual Round",
                Start = now.AddHours(-4),
                End = now.AddHours(-3),
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
                Name = "Team Judge",
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
                Status = SubmissionStatusEnum.Pending.ToString(),
                Score = 10,
                CreatedAt = now
            };

            var leaderboardEntry = new LeaderboardEntry
            {
                EntryId = Guid.NewGuid(),
                ContestId = contest.ContestId,
                TeamId = team.TeamId,
                Rank = 1,
                Score = 0,
                SnapshotAt = now
            };

            db.Users.AddRange(organizerUser, mentorUser, studentUser);
            db.Mentors.Add(mentor);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.Submissions.Add(submission);
            db.LeaderboardEntries.Add(leaderboardEntry);
            db.Configs.AddRange(
                new Config
                {
                    Key = ConfigKeys.RoundAppealSubmitDeadlineUtc(round.RoundId),
                    Value = now.AddHours(2).ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                },
                new Config
                {
                    Key = ConfigKeys.RoundAppealReviewDeadlineUtc(round.RoundId),
                    Value = now.AddHours(4).ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                });
            db.SaveChanges();

            return new SeedData(
                RoundId: round.RoundId,
                SubmissionId: submission.SubmissionId,
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
        public async Task JudgeDeadline_End_ShouldAutoScorePendingSubmissions()
        {
            var now = DateTime.UtcNow;
            var seed = SeedRoundWithPendingSubmission(now);
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/time-travel/judge-deadline-end");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var updated = await db.Submissions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SubmissionId == seed.SubmissionId);

            updated.Should().NotBeNull();
            updated!.Status.Should().Be(SubmissionStatusEnum.Finished.ToString());
            updated.Score.Should().Be(0);
        }
    }
}
