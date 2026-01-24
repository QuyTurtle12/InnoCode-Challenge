using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Submissions
{
    public class PlagiarismDetectionApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public PlagiarismDetectionApiTests(ApiFactory factory)
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
            string StudentEmail,
            string StudentPassword,
            Guid StudentId,
            Guid RoundId,
            Guid ProblemId,
            Guid TeamId,
            Guid OrganizerUserId,
            string Code);

        private PlagiarismSeed SeedPlagiarismScenario()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            const string studentPassword = "P@ssword123!";
            var studentEmail = $"student{Guid.NewGuid():N}@test.com";
            var studentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Student",
                Email = studentEmail,
                PasswordHash = PasswordHasher.Hash(studentPassword),
                Role = RoleConstants.Student,
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

            var student = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = studentUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var organizerUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Organizer",
                Email = $"org{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
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
                Type = ProblemTypeEnum.Manual.ToString(),
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

            var teamMember = new TeamMember
            {
                TeamId = teamId,
                StudentId = student.StudentId,
                MemberRole = "leader",
                JoinedAt = now
            };

            var otherMentor = new Mentor
            {
                MentorId = Guid.NewGuid(),
                UserId = otherMentorUser.UserId,
                SchoolId = TestSeed.SchoolId,
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

            var code = string.Concat(Enumerable.Repeat("a=0\n", 80));
            var normalized = PlagiarismHelpers.NormalizePython(code, removeTripleQuoted: true);
            var hash = PlagiarismHelpers.Sha256Hex(normalized);

            var existingSubmission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = otherTeamId,
                ProblemId = problemId,
                SubmittedByStudentId = Guid.NewGuid(),
                Status = SubmissionStatusEnum.Finished.ToString(),
                Score = 100,
                CreatedAt = now
            };

            var otherStudent = new Student
            {
                StudentId = existingSubmission.SubmittedByStudentId,
                UserId = otherStudentUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var existingFingerprint = new SubmissionFingerprint
            {
                FingerprintId = Guid.NewGuid(),
                SubmissionId = existingSubmission.SubmissionId,
                ProblemId = problemId,
                TeamId = otherTeamId,
                Algorithm = "sha256_py_v1",
                Hash = hash,
                NormalizedLength = normalized.Length,
                CreatedAt = now
            };

            db.Users.AddRange(studentUser, mentorUser, otherMentorUser, otherStudentUser, organizerUser);
            db.Mentors.AddRange(mentor, otherMentor);
            db.Students.AddRange(student, otherStudent);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.AddRange(team, otherTeam);
            db.TeamMembers.Add(teamMember);
            db.Submissions.Add(existingSubmission);
            db.SubmissionFingerprints.Add(existingFingerprint);
            db.SaveChanges();

        return new PlagiarismSeed(
            StudentEmail: studentEmail,
            StudentPassword: studentPassword,
            StudentId: student.StudentId,
            RoundId: roundId,
            ProblemId: problemId,
            TeamId: teamId,
            OrganizerUserId: organizerUser.UserId,
            Code: code);
        }

        private PlagiarismSeed SeedNoMatchScenario()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            const string studentPassword = "P@ssword123!";
            var studentEmail = $"student{Guid.NewGuid():N}@test.com";
            var studentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Student",
                Email = studentEmail,
                PasswordHash = PasswordHasher.Hash(studentPassword),
                Role = RoleConstants.Student,
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

            var organizerUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Organizer",
                Email = $"org{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.ContestOrganizer,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
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
                Type = ProblemTypeEnum.Manual.ToString(),
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

            var teamMember = new TeamMember
            {
                TeamId = teamId,
                StudentId = student.StudentId,
                MemberRole = "leader",
                JoinedAt = now
            };

            var code = string.Concat(Enumerable.Repeat("b=1\n", 80));

            db.Users.AddRange(studentUser, mentorUser, organizerUser);
            db.Mentors.Add(mentor);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.SaveChanges();

        return new PlagiarismSeed(
            StudentEmail: studentEmail,
            StudentPassword: studentPassword,
            StudentId: student.StudentId,
            RoundId: roundId,
            ProblemId: problemId,
            TeamId: teamId,
            OrganizerUserId: organizerUser.UserId,
            Code: code);
        }

        private static byte[] BuildZipBytes(string code)
        {
            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("main.py");
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(code);
                entryStream.Write(bytes, 0, bytes.Length);
            }
            ms.Position = 0;
            return ms.ToArray();
        }

        private static MultipartFormDataContent BuildZipUpload(string code)
        {
            var zipBytes = BuildZipBytes(code);

            var fileContent = new ByteArrayContent(zipBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");

            var form = new MultipartFormDataContent();
            form.Add(fileContent, "file", "submission.zip");
            return form;
        }

        private static void RegisterZipResponse(string url, string code)
        {
            var zipBytes = BuildZipBytes(code);
            FakeHttpClientFactory.SetResponseBytes(url, zipBytes, "application/zip");
        }

        [Fact]
        public async Task FinishManualRound_WhenFingerprintMatches_ShouldFlagPlagiarismSuspected()
        {
            var seed = SeedPlagiarismScenario();
            var token = await LoginAsync(seed.StudentEmail, seed.StudentPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/manual-test/submissions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = BuildZipUpload(seed.Code);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            Guid submissionId;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var submission = db.Submissions
                .Where(s => s.ProblemId == seed.ProblemId && s.SubmittedByStudentId == seed.StudentId)
                .OrderByDescending(s => s.CreatedAt)
                .First();

            submission.Status.Should().Be(SubmissionStatusEnum.Pending.ToString());
            submission.JudgedBy.Should().BeNull();
            submissionId = submission.SubmissionId;

            var artifact = db.SubmissionArtifacts.First(a => a.SubmissionId == submissionId);
            RegisterZipResponse(artifact.Url, seed.Code);

            var finishReq = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/finish");
            finishReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var finishRes = await _client.SendAsync(finishReq);
            finishRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var updated = verifyDb.Submissions.First(s => s.SubmissionId == submissionId);
            updated.Status.Should().Be(SubmissionStatusEnum.PlagiarismSuspected.ToString());

            var fingerprint = verifyDb.SubmissionFingerprints.First(f => f.SubmissionId == submissionId);
            fingerprint.Hash.Should().NotBeNullOrWhiteSpace();

            verifyDb.Notifications.Any(n => n.UserId == seed.OrganizerUserId && n.Type == NotificationTypes.PlagiarismSuspected)
                .Should().BeTrue();
        }

        [Fact]
        public async Task FinishManualRound_WhenNoMatch_ShouldRemainPending()
        {
            var seed = SeedNoMatchScenario();
            var token = await LoginAsync(seed.StudentEmail, seed.StudentPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/manual-test/submissions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = BuildZipUpload(seed.Code);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            Guid submissionId;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var submission = db.Submissions
                .Where(s => s.ProblemId == seed.ProblemId && s.SubmittedByStudentId == seed.StudentId)
                .OrderByDescending(s => s.CreatedAt)
                .First();

            submission.Status.Should().Be(SubmissionStatusEnum.Pending.ToString());
            submission.JudgedBy.Should().BeNull();
            submissionId = submission.SubmissionId;

            var artifact = db.SubmissionArtifacts.First(a => a.SubmissionId == submissionId);
            RegisterZipResponse(artifact.Url, seed.Code);

            var finishReq = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/finish");
            finishReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var finishRes = await _client.SendAsync(finishReq);
            finishRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var updated = verifyDb.Submissions.First(s => s.SubmissionId == submissionId);
            updated.Status.Should().Be(SubmissionStatusEnum.Pending.ToString());

            verifyDb.SubmissionFingerprints.Any(f => f.SubmissionId == submissionId)
                .Should().BeTrue();
        }
    }
}
