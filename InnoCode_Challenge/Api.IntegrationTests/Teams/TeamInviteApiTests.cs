using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.TeamInviteDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Teams
{
    public class TeamInviteApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public TeamInviteApiTests(ApiFactory factory)
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

        private sealed record SeedData(
            Guid TeamId,
            Guid ContestId,
            Guid InvitedStudentId,
            string MentorEmail,
            string MentorPassword,
            string InvitedStudentEmail);

        private SeedData SeedBase()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            const string mentorPassword = "P@ssword123!";
            var mentorEmail = $"mentor{Guid.NewGuid():N}@test.com";
            var mentorUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Team Mentor",
                Email = mentorEmail,
                PasswordHash = PasswordHasher.Hash(mentorPassword),
                Role = RoleConstants.Mentor,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var studentEmail = $"student{Guid.NewGuid():N}@test.com";
            var studentUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Team Member",
                Email = studentEmail,
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.Student,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var invitedEmail = $"invited{Guid.NewGuid():N}@test.com";
            var invitedUser = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Invited Student",
                Email = invitedEmail,
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

            var invitedStudent = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = invitedUser.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            var contestId = Guid.NewGuid();
            var contest = new Contest
            {
                ContestId = contestId,
                Name = "Team Invite Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = null
            };

            var round = new Round
            {
                RoundId = Guid.NewGuid(),
                ContestId = contestId,
                Name = "Round 1",
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                Status = RoundStatusEnum.Opened.ToString(),
                IsRetakeRound = false
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

            db.Users.AddRange(mentorUser, studentUser, invitedUser);
            db.Mentors.Add(mentor);
            db.Students.AddRange(student, invitedStudent);
            db.Contests.Add(contest);
            db.Rounds.Add(round);
            db.Teams.Add(team);
            db.TeamMembers.Add(teamMember);
            db.SaveChanges();

            return new SeedData(
                TeamId: teamId,
                ContestId: contestId,
                InvitedStudentId: invitedStudent.StudentId,
                MentorEmail: mentorEmail,
                MentorPassword: mentorPassword,
                InvitedStudentEmail: invitedEmail);
        }

        [Fact]
        public async Task CreateInvite_ByEmail_ShouldReturnPendingInvite()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                InviteeEmail = seed.InvitedStudentEmail
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await res.ReadOkAsync<TeamInviteCreatedDTO>();
            body.Data!.Status.Should().Be("pending");
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task CreateInvite_WhenTeamFull_ShouldReturn409_TEAM_FULL()
        {
            var seed = SeedBase();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                db.Configs.Add(new Config
                {
                    Key = ConfigKeys.ContestTeamMembersMax(seed.ContestId),
                    Value = "1",
                    Scope = "contest",
                    UpdatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                InviteeEmail = seed.InvitedStudentEmail
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("TEAM_FULL");
        }

        [Fact]
        public async Task Resend_WhenPending_ShouldRefreshToken()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                InviteeEmail = seed.InvitedStudentEmail
            });

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            var resendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites/{created.Data!.InviteId:D}/resend");
            resendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resendRes = await _client.SendAsync(resendReq);
            resendRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var resent = await resendRes.ReadOkAsync<TeamInviteCreatedDTO>();
            resent.Data!.Token.Should().NotBe(created.Data!.Token);
        }

        [Fact]
        public async Task Revoke_WhenPending_ShouldSetRevoked()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                InviteeEmail = seed.InvitedStudentEmail
            });

            var createRes = await _client.SendAsync(createReq);
            createRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            var revokeReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{seed.TeamId:D}/invites/{created.Data!.InviteId:D}");
            revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var revokeRes = await _client.SendAsync(revokeReq);
            revokeRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var invite = db.TeamInvites.First(i => i.InviteId == created.Data!.InviteId);
            invite.Status.Should().Be("revoked");
        }

        [Fact]
        public async Task AcceptByToken_ShouldJoinTeamAndMarkAccepted()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                StudentId = seed.InvitedStudentId
            });

            var createRes = await _client.SendAsync(createReq);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            var acceptRes = await _client.PostAsync($"/api/team-invites/accept?token={created.Data!.Token}&email={seed.InvitedStudentEmail}", null);
            acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.TeamMembers.Any(m => m.TeamId == seed.TeamId && m.StudentId == seed.InvitedStudentId).Should().BeTrue();

            var invite = db.TeamInvites.First(i => i.InviteId == created.Data!.InviteId);
            invite.Status.Should().Be("accepted");
        }

        [Fact]
        public async Task DeclineByToken_ShouldMarkCancelled()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                StudentId = seed.InvitedStudentId
            });

            var createRes = await _client.SendAsync(createReq);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            var declineRes = await _client.PostAsync($"/api/team-invites/decline?token={created.Data!.Token}&email={seed.InvitedStudentEmail}", null);
            declineRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var invite = db.TeamInvites.First(i => i.InviteId == created.Data!.InviteId);
            invite.Status.Should().Be("cancelled");
        }

        [Fact]
        public async Task AcceptByToken_WhenEmailMismatch_ShouldReturn403_EMAIL_MISMATCH()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                StudentId = seed.InvitedStudentId
            });

            var createRes = await _client.SendAsync(createReq);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            var res = await _client.PostAsync($"/api/team-invites/accept?token={created.Data!.Token}&email=wrong@example.com", null);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_MISMATCH");
        }

        [Fact]
        public async Task AcceptByToken_WhenExpired_ShouldReturn410_INVITE_EXPIRED()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                StudentId = seed.InvitedStudentId
            });

            var createRes = await _client.SendAsync(createReq);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var invite = db.TeamInvites.First(i => i.InviteId == created.Data!.InviteId);
                invite.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
                db.SaveChanges();
            }

            var res = await _client.PostAsync($"/api/team-invites/accept?token={created.Data!.Token}&email={seed.InvitedStudentEmail}", null);
            res.StatusCode.Should().Be(HttpStatusCode.Gone);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVITE_EXPIRED");
        }

        [Fact]
        public async Task ListInvites_WhenStatusFilter_ShouldReturnMatchingOnly()
        {
            var seed = SeedBase();
            var token = await LoginAsync(seed.MentorEmail, seed.MentorPassword);

            var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/teams/{seed.TeamId:D}/invites");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTeamInviteDTO
            {
                InviteeEmail = seed.InvitedStudentEmail
            });

            var createRes = await _client.SendAsync(createReq);
            var created = await createRes.ReadOkAsync<TeamInviteCreatedDTO>();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var invite = db.TeamInvites.First(i => i.InviteId == created.Data!.InviteId);
                invite.Status = "revoked";
                db.SaveChanges();
            }

            var listReq = new HttpRequestMessage(HttpMethod.Get, $"/api/teams/{seed.TeamId:D}/invites?status=pending");
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var listRes = await _client.SendAsync(listReq);
            listRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var list = await listRes.ReadOkAsync<List<TeamInviteDTO>>();
            list.Data!.Any().Should().BeFalse();
        }
    }
}
