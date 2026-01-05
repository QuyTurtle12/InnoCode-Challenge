using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Submissions
{
    public class PlagiarismResolutionApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public PlagiarismResolutionApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record SeedData(
            Guid SubmissionId,
            Guid TeamId,
            Guid ContestId,
            string OrganizerEmail,
            string OrganizerPassword,
            string StudentEmail,
            string StudentPassword);

        private SeedData SeedPlagiarismCase(string problemType = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            const string organizerPassword = "P@ssword123!";
            const string studentPassword = "P@ssword123!";
            var organizerEmail = $"org{Guid.NewGuid():N}@test.com";
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

            var studentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Student",
                Email = $"student{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash(studentPassword),
                Role = RoleConstants.Student,
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

            var contestId = Guid.NewGuid();
            var contest = new Contest
            {
                ContestId = contestId,
                Name = "Plagiarism Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/img.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(5),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var roundId = Guid.NewGuid();
            var round = new Round
            {
                RoundId = roundId,
                ContestId = contestId,
                Name = "Plagiarism Round",
                Start = now.AddHours(-1),
                End = now.AddHours(2),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problemId = Guid.NewGuid();
            var problem = new Problem
            {
                ProblemId = problemId,
                RoundId = roundId,
                Language = "markdown",
                Type = problemType ?? ProblemTypeEnum.Manual.ToString(),
                CreatedAt = now
            };

            var teamId = Guid.NewGuid();
            var team = new Team
            {
                TeamId = teamId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "Team P",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var teamMember = new TeamMember
            {
                TeamId = teamId,
                StudentId = student.StudentId,
                MemberRole = "leader",
                JoinedAt = now
            };

            var submissionId = Guid.NewGuid();
            var submission = new Submission
            {
                SubmissionId = submissionId,
                TeamId = teamId,
                ProblemId = problemId,
                SubmittedByStudentId = student.StudentId,
                Status = SubmissionStatusEnum.PlagiarismSuspected.ToString(),
                Score = 50,
                CreatedAt = now
            };

            var leaderboard = new LeaderboardEntry
            {
                ContestId = contestId,
                TeamId = teamId,
                Score = 50,
                SnapshotAt = now
            };

            db.Users.AddRange(organizerUser, studentUser, mentorUser);
            db.Students.Add(student);
            db.Mentors.Add(mentor);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.Submissions.Add(submission);
            db.LeaderboardEntries.Add(leaderboard);
            db.SaveChanges();

            return new SeedData(
                SubmissionId: submissionId,
                TeamId: teamId,
                ContestId: contestId,
                OrganizerEmail: organizerEmail,
                OrganizerPassword: organizerPassword,
                StudentEmail: studentUser.Email,
                StudentPassword: studentPassword);
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
        public async Task Deny_ShouldEliminateTeam_AndZeroLeaderboard()
        {
            var seed = SeedPlagiarismCase();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/deny");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var team = db.Teams.First(t => t.TeamId == seed.TeamId);
            team.Status.Should().Be(TeamStatusConstants.Eliminated);

            var entry = db.LeaderboardEntries.First(e => e.TeamId == seed.TeamId && e.ContestId == seed.ContestId);
            entry.Score.Should().Be(0);

            var submission = db.Submissions.First(s => s.SubmissionId == seed.SubmissionId);
            submission.Status.Should().Be(SubmissionStatusEnum.PlagiarismConfirmed.ToString());
        }

        [Fact]
        public async Task Approve_ShouldRestoreStatus_AndKeepScore()
        {
            var seed = SeedPlagiarismCase(problemType: ProblemTypeEnum.Manual.ToString());
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/approve");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var team = db.Teams.First(t => t.TeamId == seed.TeamId);
            team.Status.Should().Be(TeamStatusConstants.Active);

            var submission = db.Submissions.First(s => s.SubmissionId == seed.SubmissionId);
            submission.Status.Should().Be(SubmissionStatusEnum.Pending.ToString());
            submission.JudgedBy.Should().BeNull();
            submission.Score.Should().Be(0);
        }

        [Fact]
        public async Task Resolve_NonSuspected_ShouldReturnBadRequest()
        {
            var seed = SeedPlagiarismCase();

            // Flip status to finished to simulate already resolved case
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var sub = db.Submissions.First(s => s.SubmissionId == seed.SubmissionId);
                sub.Status = SubmissionStatusEnum.Finished.ToString();
                db.SaveChanges();
            }

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/deny");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Student_CannotResolve_ShouldReturnForbidden()
        {
            var seed = SeedPlagiarismCase();
            var studentToken = await LoginAsync(seed.StudentEmail, seed.StudentPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/deny");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Approve_AutoSubmission_ShouldRemainFinished_KeepScore()
        {
            var seed = SeedPlagiarismCase(problemType: ProblemTypeEnum.AutoEvaluation.ToString());
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/approve");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var submission = db.Submissions.First(s => s.SubmissionId == seed.SubmissionId);
            submission.Status.Should().Be(SubmissionStatusEnum.Finished.ToString());
            submission.JudgedBy.Should().BeNull();
            submission.Score.Should().Be(50);
        }
    }
}
