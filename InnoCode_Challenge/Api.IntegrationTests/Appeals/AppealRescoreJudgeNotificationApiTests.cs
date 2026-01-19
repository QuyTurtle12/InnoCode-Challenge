using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AppealDTOs;
using Repository.DTOs.AuthDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Appeals
{
    public class AppealRescoreJudgeNotificationApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public AppealRescoreJudgeNotificationApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record AppealSeed(
            Guid RoundId,
            Guid TeamId,
            Guid StudentId,
            Guid JudgeUserId,
            string MentorEmail,
            string MentorPassword,
            string OrganizerEmail,
            string OrganizerPassword);

        private AppealSeed SeedManualAppealData()
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

            var mentorEmail = $"mentor{Guid.NewGuid():N}@test.com";
            const string mentorPassword = "P@ssword123!";
            var mentorUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Mentor",
                Email = mentorEmail,
                PasswordHash = PasswordHasher.Hash(mentorPassword),
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
                Name = "Rescore Appeal Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-2),
                End = now.AddHours(6),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var roundId = Guid.NewGuid();
            var round = new Round
            {
                RoundId = roundId,
                ContestId = contestId,
                Name = "Manual Round",
                Start = now.AddHours(-3),
                End = now.AddMinutes(-10),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
            };

            var problemId = Guid.NewGuid();
            var problem = new Problem
            {
                ProblemId = problemId,
                RoundId = roundId,
                Language = "markdown",
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
                Name = "Team Appeal",
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

            var submission = new Submission
            {
                SubmissionId = Guid.NewGuid(),
                TeamId = teamId,
                ProblemId = problemId,
                SubmittedByStudentId = student.StudentId,
                Status = SubmissionStatusEnum.Finished.ToString(),
                Score = 10,
                JudgedBy = judgeUser.UserId.ToString(),
                CreatedAt = now
            };

            var invite = new JudgeInvite
            {
                InviteId = Guid.NewGuid(),
                JudgeId = judgeUser.UserId,
                ContestId = contestId,
                InviteCode = Guid.NewGuid().ToString("N"),
                Status = JudgeInviteStatusConstants.Accepted,
                CreatedAt = now,
                AcceptedAt = now,
                CreatedBy = organizerUser.UserId.ToString()
            };

            db.Users.AddRange(organizerUser, mentorUser, studentUser, judgeUser);
            db.Mentors.Add(mentor);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.Submissions.Add(submission);
            db.JudgeInvites.Add(invite);
            db.SaveChanges();

            db.Configs.AddRange(
                new Config
                {
                    Key = ConfigKeys.RoundAppealSubmitDeadlineUtc(roundId),
                    Value = now.AddMinutes(30).ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                },
                new Config
                {
                    Key = ConfigKeys.RoundAppealReviewDeadlineUtc(roundId),
                    Value = now.AddMinutes(60).ToString("o"),
                    Scope = "contest",
                    UpdatedAt = now
                });
            db.SaveChanges();

            return new AppealSeed(
                RoundId: roundId,
                TeamId: teamId,
                StudentId: student.StudentId,
                JudgeUserId: judgeUser.UserId,
                MentorEmail: mentorEmail,
                MentorPassword: mentorPassword,
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
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private static MultipartFormDataContent BuildCreateAppealForm(Guid roundId, Guid teamId, Guid studentId)
        {
            return new MultipartFormDataContent
            {
                { new StringContent(roundId.ToString()), "RoundId" },
                { new StringContent(teamId.ToString()), "TeamId" },
                { new StringContent(studentId.ToString()), "StudentId" },
                { new StringContent("Need rescore"), "Reason" },
                { new StringContent(AppealResolutionEnum.Rescore.ToString()), "AppealResolution" }
            };
        }

        [Fact]
        public async Task ReviewAppeal_ApprovedRescore_ShouldNotifyJudge()
        {
            var seed = SeedManualAppealData();
            var mentorToken = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            using var form = BuildCreateAppealForm(seed.RoundId, seed.TeamId, seed.StudentId);
            var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/appeals");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mentorToken);
            createReq.Content = form;

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await createRes.ReadOkAsync<GetAppealDTO>();

            var organizerToken = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);
            var reviewReq = new HttpRequestMessage(HttpMethod.Put, $"/api/appeals/{created.Data!.AppealId:D}/review");
            reviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", organizerToken);
            reviewReq.Content = JsonContent.Create(new ReviewAppealDTO
            {
                Decision = "Approved",
                DecisionReason = "Rescore granted"
            });

            var reviewRes = await _client.SendAsync(reviewReq);
            reviewRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Notifications.Any(n => n.UserId == seed.JudgeUserId
                                      && n.Type == NotificationTypes.ManualGradingAssigned).Should().BeTrue();
        }
    }
}
