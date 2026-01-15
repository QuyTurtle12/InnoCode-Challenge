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
    public class AppealNotificationLogApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public AppealNotificationLogApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record AppealSeed(
            Guid ContestId,
            Guid RoundId,
            Guid TeamId,
            Guid StudentId,
            Guid StudentUserId,
            Guid MentorUserId,
            string MentorEmail,
            string MentorPassword,
            Guid OrganizerUserId,
            string OrganizerEmail,
            string OrganizerPassword);

        private AppealSeed SeedAppealData()
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
                Name = "Appeal Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var roundId = Guid.NewGuid();
            var roundEnd = now.AddMinutes(-5);
            var round = new Round
            {
                RoundId = roundId,
                ContestId = contestId,
                Name = "Round 1",
                Start = now.AddHours(-2),
                End = roundEnd,
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

            db.Users.AddRange(organizerUser, mentorUser, studentUser);
            db.Mentors.Add(mentor);
            db.Students.Add(student);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Problems.Add(problem);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.SaveChanges();

            // Keep appeal windows open for testing
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
                ContestId: contestId,
                RoundId: roundId,
                TeamId: teamId,
                StudentId: student.StudentId,
                StudentUserId: studentUser.UserId,
                MentorUserId: mentorUser.UserId,
                MentorEmail: mentorEmail,
                MentorPassword: mentorPassword,
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
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private static MultipartFormDataContent BuildCreateAppealForm(Guid roundId, Guid teamId, Guid studentId, string reason)
        {
            var content = new MultipartFormDataContent
            {
                { new StringContent(roundId.ToString()), "RoundId" },
                { new StringContent(teamId.ToString()), "TeamId" },
                { new StringContent(studentId.ToString()), "StudentId" },
                { new StringContent(reason), "Reason" },
                { new StringContent(AppealResolutionEnum.Rescore.ToString()), "AppealResolution" }
            };

            return content;
        }

        [Fact]
        public async Task CreateAppeal_ShouldNotifyOrganizer_AndLog()
        {
            var seed = SeedAppealData();
            var mentorToken = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            using var form = BuildCreateAppealForm(seed.RoundId, seed.TeamId, seed.StudentId, "Need retake");
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/appeals");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mentorToken);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<GetAppealDTO>();
            body.Data!.AppealId.Should().NotBe(Guid.Empty);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Notifications.Any(n => n.UserId == seed.OrganizerUserId
                                      && n.Type == NotificationTypes.AppealCreated).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.MentorUserId
                                     && l.Action == ActivityActions.AppealSubmit
                                     && l.TargetType == TargetTypes.Appeal
                                     && l.TargetId == body.Data!.AppealId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task ReviewAppeal_ShouldNotifyOwnerAndMentor_AndLog()
        {
            var seed = SeedAppealData();
            var mentorToken = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            using var form = BuildCreateAppealForm(seed.RoundId, seed.TeamId, seed.StudentId, "Need retake");
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
                Decision = "Rejected",
                DecisionReason = "Not sufficient"
            });

            var reviewRes = await _client.SendAsync(reviewReq);
            reviewRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Notifications.Any(n => n.UserId == seed.StudentUserId
                                      && n.Type == NotificationTypes.AppealUpdated).Should().BeTrue();
            db.Notifications.Any(n => n.UserId == seed.MentorUserId
                                      && n.Type == NotificationTypes.AppealUpdated).Should().BeTrue();

            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.AppealResolve
                                     && l.TargetType == TargetTypes.Appeal
                                     && l.TargetId == created.Data!.AppealId.ToString()).Should().BeTrue();
        }
    }
}
