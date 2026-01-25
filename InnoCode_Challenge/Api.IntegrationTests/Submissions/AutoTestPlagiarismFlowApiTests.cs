using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.JudgeDTOs;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Submissions
{
    public class AutoTestPlagiarismFlowApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public AutoTestPlagiarismFlowApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record AutoTestSeed(
            string StudentEmail,
            string StudentPassword,
            Guid StudentId,
            Guid RoundId,
            Guid ProblemId,
            Guid TeamId,
            string Code);

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

        private AutoTestSeed SeedAutoTestScenario(bool withMockTest)
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
                Name = "Auto Test Contest",
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
                Type = ProblemTypeEnum.AutoEvaluation.ToString(),
                MockTestUrl = withMockTest ? "https://example.test/mock_test.py" : null,
                CreatedAt = now
            };

            var testCase = new TestCase
            {
                TestCaseId = Guid.NewGuid(),
                ProblemId = problemId,
                Type = TestCaseTypeEnum.TestCase.ToString(),
                Weight = 1,
                Input = "1",
                ExpectedOutput = "1"
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

            var otherTeamId = Guid.NewGuid();
            var otherTeam = new Team
            {
                TeamId = otherTeamId,
                ContestId = contestId,
                SchoolId = TestSeed.SchoolId,
                MentorId = mentor.MentorId,
                Name = "Team B",
                Status = TeamStatusConstants.Active,
                CreatedAt = now
            };

            var otherTeamMember = new TeamMember
            {
                TeamId = otherTeamId,
                StudentId = otherStudent.StudentId,
                MemberRole = "leader",
                JoinedAt = now
            };

            var code = string.Concat(Enumerable.Repeat("a=0\n", 80));
            var normalized = PlagiarismHelpers.NormalizePythonForFingerprint(code, removeTripleQuoted: true);
            var hash = PlagiarismHelpers.Sha256Hex(normalized);

            var existingSubmission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = otherTeamId,
                ProblemId = problemId,
                SubmittedByStudentId = otherStudent.StudentId,
                Status = SubmissionStatusEnum.Finished.ToString(),
                Score = 100,
                CreatedAt = now
            };

            var existingFingerprint = new SubmissionFingerprint
            {
                FingerprintId = Guid.NewGuid(),
                SubmissionId = existingSubmission.SubmissionId,
                ProblemId = problemId,
                TeamId = otherTeamId,
                Algorithm = "sha256_py_v2",
                Hash = hash,
                NormalizedLength = normalized.Length,
                CreatedAt = now
            };

            var leaderEntryA = new LeaderboardEntry
            {
                EntryId = Guid.NewGuid(),
                ContestId = contestId,
                TeamId = teamId,
                Score = 0,
                Rank = 1,
                SnapshotAt = now
            };

            var leaderEntryB = new LeaderboardEntry
            {
                EntryId = Guid.NewGuid(),
                ContestId = contestId,
                TeamId = otherTeamId,
                Score = 0,
                Rank = 2,
                SnapshotAt = now
            };

            db.Users.AddRange(studentUser, mentorUser, otherStudentUser, organizerUser);
            db.Mentors.Add(mentor);
            db.Students.AddRange(student, otherStudent);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.TestCases.Add(testCase);
            db.Teams.AddRange(team, otherTeam);
            db.TeamMembers.AddRange(teamMember, otherTeamMember);
            db.Submissions.Add(existingSubmission);
            db.SubmissionFingerprints.Add(existingFingerprint);
            db.LeaderboardEntries.AddRange(leaderEntryA, leaderEntryB);
            db.SaveChanges();

            return new AutoTestSeed(
                StudentEmail: studentEmail,
                StudentPassword: studentPassword,
                StudentId: student.StudentId,
                RoundId: roundId,
                ProblemId: problemId,
                TeamId: teamId,
                Code: code);
        }

        private static MultipartFormDataContent BuildCodeForm(string code)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(code), "Code");
            form.Add(new StringContent(TestCaseEvaluationTypeEnum.Code.ToString()), "type");
            return form;
        }

        private sealed class LocalCodeServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly CancellationTokenSource _cts;
            private readonly Task _loopTask;
            public string Url { get; }

            public LocalCodeServer(string code)
            {
                int port = GetFreePort();
                Url = $"http://localhost:{port}/code.py";
                var prefix = $"http://localhost:{port}/";

                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();

                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(async () =>
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        HttpListenerContext context;
                        try
                        {
                            context = await _listener.GetContextAsync();
                        }
                        catch (HttpListenerException)
                        {
                            break;
                        }

                        var bytes = Encoding.UTF8.GetBytes(code);
                        context.Response.ContentType = "text/plain";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        context.Response.OutputStream.Close();
                    }
                });
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _listener.Close();
                try
                {
                    _loopTask.GetAwaiter().GetResult();
                }
                catch
                {
                }
                _cts.Dispose();
            }

            private static int GetFreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }

        [Fact]
        public async Task AutoTestSubmission_Acceptance_ShouldAllow_WhenPlagiarismSuspected()
        {
            var seed = SeedAutoTestScenario(withMockTest: false);
            var token = await LoginAsync(seed.StudentEmail, seed.StudentPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/auto-test/submissions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = BuildCodeForm(seed.Code);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<JudgeSubmissionResultDTO>();
            body.Data.Should().NotBeNull();

            Guid submissionId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var submission = db.Submissions
                    .Where(s => s.ProblemId == seed.ProblemId && s.SubmittedByStudentId == seed.StudentId)
                    .OrderByDescending(s => s.CreatedAt)
                    .First();

                submission.Status.Should().Be(SubmissionStatusEnum.Finished.ToString());
                db.SubmissionFingerprints.Any(f => f.SubmissionId == submission.SubmissionId)
                    .Should().BeFalse();

                submissionId = submission.SubmissionId;
            }

            using var server = new LocalCodeServer(seed.Code);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var artifact = db.SubmissionArtifacts.First(a => a.SubmissionId == submissionId);
                artifact.Url = server.Url;
                db.SaveChanges();
            }

            var acceptReq = new HttpRequestMessage(HttpMethod.Put, $"/api/submissions/{submissionId:D}/acceptance");
            acceptReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var acceptRes = await _client.SendAsync(acceptReq);
            acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var submission = db.Submissions.First(s => s.SubmissionId == submissionId);
                submission.Status.Should().Be(SubmissionStatusEnum.PlagiarismSuspected.ToString());
                db.SubmissionFingerprints.Any(f => f.SubmissionId == submissionId)
                    .Should().BeTrue();
            }
        }

        [Fact]
        public async Task MockTestSubmission_Acceptance_ShouldAllow_WhenPlagiarismSuspected()
        {
            var seed = SeedAutoTestScenario(withMockTest: true);
            var token = await LoginAsync(seed.StudentEmail, seed.StudentPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rounds/{seed.RoundId:D}/auto-test/mock-test/submissions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = BuildCodeForm(seed.Code);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<JudgeSubmissionResultDTO>();
            body.Data.Should().NotBeNull();

            Guid submissionId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var submission = db.Submissions
                    .Where(s => s.ProblemId == seed.ProblemId && s.SubmittedByStudentId == seed.StudentId)
                    .OrderByDescending(s => s.CreatedAt)
                    .First();

                submission.Status.Should().Be(SubmissionStatusEnum.Finished.ToString());
                db.SubmissionFingerprints.Any(f => f.SubmissionId == submission.SubmissionId)
                    .Should().BeFalse();

                submissionId = submission.SubmissionId;
            }

            using var server = new LocalCodeServer(seed.Code);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var artifact = db.SubmissionArtifacts.First(a => a.SubmissionId == submissionId);
                artifact.Url = server.Url;
                db.SaveChanges();
            }

            var acceptReq = new HttpRequestMessage(HttpMethod.Put, $"/api/submissions/{submissionId:D}/acceptance");
            acceptReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var acceptRes = await _client.SendAsync(acceptReq);
            acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var submission = db.Submissions.First(s => s.SubmissionId == submissionId);
                submission.Status.Should().Be(SubmissionStatusEnum.PlagiarismSuspected.ToString());
                db.SubmissionFingerprints.Any(f => f.SubmissionId == submissionId)
                    .Should().BeTrue();
            }
        }
    }
}
