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

namespace Api.IntegrationTests.Students
{
    public class TeamMemberRemovalApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public TeamMemberRemovalApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record UserSeed(Guid UserId, string Email, string Password);

        private UserSeed SeedMentorUser(string emailPrefix)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var email = $"{emailPrefix}{Guid.NewGuid():N}@test.com";
            const string password = "P@ssword123!";

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Mentor",
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = RoleConstants.Mentor,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var mentor = new Mentor
            {
                MentorId = Guid.NewGuid(),
                UserId = user.UserId,
                SchoolId = TestSeed.SchoolId,
                CreatedAt = now
            };

            db.Users.Add(user);
            db.Mentors.Add(mentor);
            db.SaveChanges();

            return new UserSeed(user.UserId, email, password);
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

        private (Guid TeamId, Guid StudentId) SeedTeamWithMember(Guid mentorUserId, bool contestStarted)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var mentor = db.Mentors.First(m => m.UserId == mentorUserId);

            var contest = new Contest
            {
                ContestId = Guid.NewGuid(),
                Name = $"Contest {Guid.NewGuid():N}",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = contestStarted ? ContestStatusEnum.Ongoing.ToString() : ContestStatusEnum.RegistrationOpen.ToString(),
                Start = contestStarted ? now.AddHours(-2) : now.AddHours(2),
                End = now.AddHours(3),
                CreatedAt = now,
                CreatedBy = Guid.NewGuid().ToString()
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

            var student = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = studentUser.UserId,
                SchoolId = TestSeed.SchoolId,
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

            var member = new TeamMember
            {
                TeamId = team.TeamId,
                StudentId = student.StudentId,
                MemberRole = "Leader",
                JoinedAt = now
            };

            db.Users.Add(studentUser);
            db.Contests.Add(contest);
            db.Students.Add(student);
            db.Teams.Add(team);
            db.TeamMembers.Add(member);
            db.SaveChanges();

            return (team.TeamId, student.StudentId);
        }

        [Fact]
        public async Task RemoveMember_WhenMentorOwner_ShouldReturn204_AndRemoveMember()
        {
            var mentor = SeedMentorUser("mentor");
            var token = await LoginAsync(mentor.Email, mentor.Password);

            var (teamId, studentId) = SeedTeamWithMember(mentor.UserId, contestStarted: false);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId:D}/members/{studentId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.TeamMembers.Any(tm => tm.TeamId == teamId && tm.StudentId == studentId).Should().BeFalse();
        }

        [Fact]
        public async Task RemoveMember_WhenContestStarted_ShouldReturn409_CONFLICT()
        {
            var mentor = SeedMentorUser("mentor");
            var token = await LoginAsync(mentor.Email, mentor.Password);

            var (teamId, studentId) = SeedTeamWithMember(mentor.UserId, contestStarted: true);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId:D}/members/{studentId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.CONFLICT);
        }

        [Fact]
        public async Task RemoveMember_WhenMentorNotOwner_ShouldReturn403_FORBIDDEN()
        {
            var mentor = SeedMentorUser("mentor");
            var otherMentor = SeedMentorUser("other");
            var token = await LoginAsync(otherMentor.Email, otherMentor.Password);

            var (teamId, studentId) = SeedTeamWithMember(mentor.UserId, contestStarted: false);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId:D}/members/{studentId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.FORBIDDEN);
        }

        [Fact]
        public async Task RemoveMember_WhenStudentNotInTeam_ShouldReturn404_NOT_FOUND()
        {
            var mentor = SeedMentorUser("mentor");
            var token = await LoginAsync(mentor.Email, mentor.Password);

            var (teamId, _) = SeedTeamWithMember(mentor.UserId, contestStarted: false);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId:D}/members/{Guid.NewGuid():D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.NOT_FOUND);
        }
    }
}
