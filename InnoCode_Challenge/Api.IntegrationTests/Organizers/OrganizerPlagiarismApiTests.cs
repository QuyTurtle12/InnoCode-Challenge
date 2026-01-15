using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.PlagiarismDTOs;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Organizers
{
    public class OrganizerPlagiarismApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public OrganizerPlagiarismApiTests(ApiFactory factory)
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
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private sealed record PlagiarismSeed(
            string OrganizerEmail,
            string OrganizerPassword,
            Guid OrganizerUserId,
            Guid StudentUserId,
            Guid SubmissionId,
            Guid ContestId,
            Guid TeamId,
            double Score);

        private sealed record PlagiarismMatchSeed(
            string OrganizerEmail,
            string OrganizerPassword,
            Guid OrganizerUserId,
            Guid SubmissionId,
            Guid MatchedSubmissionId,
            Guid ContestId,
            string Hash);

        private PlagiarismSeed SeedSuspectedSubmission(double score = 80)
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

            var contestId = Guid.NewGuid();
            var contest = new Contest
            {
                ContestId = contestId,
                Name = "Plagiarism Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var roundId = Guid.NewGuid();
            var round = new Round
            {
                RoundId = roundId,
                ContestId = contestId,
                Name = "Round 1",
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problemId = Guid.NewGuid();
            var problem = new Problem
            {
                ProblemId = problemId,
                RoundId = roundId,
                Language = "python",
                Type = "Open",
                CreatedAt = now
            };

            var teamId = Guid.NewGuid();
            var team = new Team
            {
                TeamId = teamId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "Team Plagiarism",
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
                Status = "PlagiarismSuspected",
                Score = score,
                CreatedAt = now
            };

            var leaderboardEntry = new LeaderboardEntry
            {
                EntryId = Guid.NewGuid(),
                ContestId = contestId,
                TeamId = teamId,
                Score = 0,
                Rank = 1,
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
            db.SaveChanges();

            return new PlagiarismSeed(
                OrganizerEmail: organizerEmail,
                OrganizerPassword: organizerPassword,
                OrganizerUserId: organizerUser.UserId,
                StudentUserId: studentUser.UserId,
                SubmissionId: submissionId,
                ContestId: contestId,
                TeamId: teamId,
                Score: score);
        }

        private PlagiarismMatchSeed SeedSuspectedWithMatch()
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

            var otherMentorUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Other Mentor",
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

            var otherStudentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Other Student",
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

            var otherMentor = new Mentor
            {
                MentorId = Guid.NewGuid(),
                UserId = otherMentorUser.UserId,
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

            var otherStudent = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = otherStudentUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var contestId = Guid.NewGuid();
            var contest = new Contest
            {
                ContestId = contestId,
                Name = "Plagiarism Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var roundId = Guid.NewGuid();
            var round = new Round
            {
                RoundId = roundId,
                ContestId = contestId,
                Name = "Round 1",
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problemId = Guid.NewGuid();
            var problem = new Problem
            {
                ProblemId = problemId,
                RoundId = roundId,
                Language = "python",
                Type = "Open",
                CreatedAt = now
            };

            var teamId = Guid.NewGuid();
            var team = new Team
            {
                TeamId = teamId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "Team A",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var otherTeamId = Guid.NewGuid();
            var otherTeam = new Team
            {
                TeamId = otherTeamId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = otherMentor.MentorId,
                Name = "Team B",
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

            var otherTeamMember = new TeamMember
            {
                TeamId = otherTeamId,
                StudentId = otherStudent.StudentId,
                MemberRole = "leader",
                JoinedAt = now
            };

            var code = string.Concat(Enumerable.Repeat("a=0\n", 80));
            var normalized = PlagiarismHelpers.NormalizePython(code, removeTripleQuoted: true);
            var hash = PlagiarismHelpers.Sha256Hex(normalized);

            var submissionId = Guid.NewGuid();
            var submission = new Submission
            {
                SubmissionId = submissionId,
                TeamId = teamId,
                ProblemId = problemId,
                SubmittedByStudentId = student.StudentId,
                Status = "PlagiarismSuspected",
                Score = 70,
                CreatedAt = now
            };

            var matchedSubmissionId = Guid.NewGuid();
            var matchedSubmission = new Submission
            {
                SubmissionId = matchedSubmissionId,
                TeamId = otherTeamId,
                ProblemId = problemId,
                SubmittedByStudentId = otherStudent.StudentId,
                Status = SubmissionStatusEnum.Finished.ToString(),
                Score = 95,
                CreatedAt = now.AddMinutes(-10)
            };

            var fp = new SubmissionFingerprint
            {
                FingerprintId = Guid.NewGuid(),
                SubmissionId = submissionId,
                ProblemId = problemId,
                TeamId = teamId,
                Algorithm = "sha256_py_v1",
                Hash = hash,
                NormalizedLength = normalized.Length,
                CreatedAt = now
            };

            var matchedFp = new SubmissionFingerprint
            {
                FingerprintId = Guid.NewGuid(),
                SubmissionId = matchedSubmissionId,
                ProblemId = problemId,
                TeamId = otherTeamId,
                Algorithm = "sha256_py_v1",
                Hash = hash,
                NormalizedLength = normalized.Length,
                CreatedAt = now.AddMinutes(-10)
            };

            db.Users.AddRange(organizerUser, mentorUser, otherMentorUser, studentUser, otherStudentUser);
            db.Mentors.AddRange(mentor, otherMentor);
            db.Students.AddRange(student, otherStudent);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.AddRange(team, otherTeam);
            db.TeamMembers.AddRange(teamMember, otherTeamMember);
            db.Submissions.AddRange(submission, matchedSubmission);
            db.SubmissionFingerprints.AddRange(fp, matchedFp);
            db.SaveChanges();

            return new PlagiarismMatchSeed(
                OrganizerEmail: organizerEmail,
                OrganizerPassword: organizerPassword,
                OrganizerUserId: organizerUser.UserId,
                SubmissionId: submissionId,
                MatchedSubmissionId: matchedSubmissionId,
                ContestId: contestId,
                Hash: hash);
        }

        [Fact]
        public async Task Approve_WhenSuspected_ShouldSetFinishedAndUpdateLeaderboard()
        {
            var seed = SeedSuspectedSubmission(score: 75);
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

            var entry = db.LeaderboardEntries.First(e => e.ContestId == seed.ContestId && e.TeamId == seed.TeamId);
            entry.Score.Should().Be(seed.Score);
        }

        [Fact]
        public async Task Approve_WhenSuspected_ShouldNotifyStudent_AndLogStatusChange()
        {
            var seed = SeedSuspectedSubmission(score: 85);
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/approve");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Notifications.Any(n => n.UserId == seed.StudentUserId
                                      && n.Type == NotificationTypes.SubmissionStatusChanged).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.SubmissionStatusChange
                                     && l.TargetType == TargetTypes.Submission
                                     && l.TargetId == seed.SubmissionId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task Deny_WhenSuspected_ShouldSetConfirmedAndKeepLeaderboardScore()
        {
            var seed = SeedSuspectedSubmission(score: 90);
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/deny");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var submission = db.Submissions.First(s => s.SubmissionId == seed.SubmissionId);
            submission.Status.Should().Be("PlagiarismConfirmed");
            submission.Score.Should().Be(0);
            submission.JudgedBy.Should().NotBeNullOrWhiteSpace();

            var entry = db.LeaderboardEntries.First(e => e.ContestId == seed.ContestId && e.TeamId == seed.TeamId);
            entry.Score.Should().Be(0);
        }

        [Fact]
        public async Task Approve_WhenUnauthorized_ShouldReturn401()
        {
            var seed = SeedSuspectedSubmission();

            var res = await _client.PostAsync($"/api/organizer/plagiarism/{seed.SubmissionId:D}/approve", null);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Approve_WhenNotSuspected_ShouldReturn400_BADREQUEST()
        {
            var seed = SeedSuspectedSubmission();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var submission = db.Submissions.First(s => s.SubmissionId == seed.SubmissionId);
                submission.Status = SubmissionStatusEnum.Finished.ToString();
                db.SaveChanges();
            }

            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/approve");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task Approve_WhenDifferentOrganizer_ShouldReturn403_FORBIDDEN()
        {
            var seed = SeedSuspectedSubmission();
            var otherEmail = $"organizer{Guid.NewGuid():N}@test.com";
            const string otherPassword = "P@ssword123!";

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                db.Users.Add(new User
                {
                    UserId = Guid.NewGuid(),
                    Fullname = "Other Organizer",
                    Email = otherEmail,
                    PasswordHash = PasswordHasher.Hash(otherPassword),
                    Role = RoleConstants.ContestOrganizer,
                    Status = UserStatusConstants.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var token = await LoginAsync(otherEmail, otherPassword);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/organizer/plagiarism/{seed.SubmissionId:D}/approve");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.FORBIDDEN);
        }

        [Fact]
        public async Task GetQueue_ShouldReturnSuspectedOnly()
        {
            var seed = SeedSuspectedWithMatch();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/organizer/plagiarism/queue?pageNumber=1&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<List<PlagiarismQueueItemDTO>>();
            body.Data!.Any(i => i.SubmissionId == seed.SubmissionId).Should().BeTrue();
            body.Data!.Any(i => i.SubmissionId == seed.MatchedSubmissionId).Should().BeFalse();
        }

        [Fact]
        public async Task GetDetail_ShouldIncludeMatchedSubmissions()
        {
            var seed = SeedSuspectedWithMatch();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/organizer/plagiarism/{seed.SubmissionId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<PlagiarismSubmissionDetailDTO>();
            body.Data!.Matches.Should().NotBeEmpty();
            body.Data!.Matches.Any(m => m.SubmissionId == seed.MatchedSubmissionId).Should().BeTrue();
        }
    }
}
