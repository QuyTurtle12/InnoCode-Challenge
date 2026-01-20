using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.CertificateDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Certificates
{
    public class CertificateIssueApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public CertificateIssueApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record Seed(
            Guid ContestId,
            Guid OtherContestId,
            Guid OrganizerUserId,
            string OrganizerEmail,
            string OrganizerPassword,
            Guid OtherOrganizerUserId,
            string OtherOrganizerEmail,
            string OtherOrganizerPassword,
            Guid TemplateId,
            Guid TeamId,
            Guid StudentId,
            Guid OtherTeamId,
            Guid OtherStudentId);

        private Seed SeedIssueData()
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

            var otherOrganizerEmail = $"organizer{Guid.NewGuid():N}@test.com";
            const string otherOrganizerPassword = "P@ssword123!";
            var otherOrganizer = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Other Organizer",
                Email = otherOrganizerEmail,
                PasswordHash = PasswordHasher.Hash(otherOrganizerPassword),
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
                Name = "Certificate Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = organizerUser.UserId.ToString()
            };

            var otherContestId = Guid.NewGuid();
            var otherContest = new Contest
            {
                ContestId = otherContestId,
                Name = "Other Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest2.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = otherOrganizer.UserId.ToString()
            };

            var templateId = Guid.NewGuid();
            var template = new CertificateTemplate
            {
                TemplateId = templateId,
                ContestId = contestId,
                Name = "Template A",
                FileUrl = "https://example.com/template.png",
                TextX = 100,
                TextY = 200,
                DeletedAt = null
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
                ContestId = otherContestId,
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

            db.Users.AddRange(organizerUser, otherOrganizer, mentorUser, studentUser, otherStudentUser);
            db.Mentors.Add(mentor);
            db.Students.AddRange(student, otherStudent);
            db.Contests.AddRange(contest, otherContest);
            db.CertificateTemplates.Add(template);
            db.Teams.AddRange(team, otherTeam);
            db.TeamMembers.AddRange(teamMember, otherTeamMember);
            db.SaveChanges();

            return new Seed(
                ContestId: contestId,
                OtherContestId: otherContestId,
                OrganizerUserId: organizerUser.UserId,
                OrganizerEmail: organizerEmail,
                OrganizerPassword: organizerPassword,
                OtherOrganizerUserId: otherOrganizer.UserId,
                OtherOrganizerEmail: otherOrganizerEmail,
                OtherOrganizerPassword: otherOrganizerPassword,
                TemplateId: templateId,
                TeamId: teamId,
                StudentId: student.StudentId,
                OtherTeamId: otherTeamId,
                OtherStudentId: otherStudent.StudentId);
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

        private async Task<IReadOnlyList<IssuedCertificateDTO>> IssueAsync(string token, object payload)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<List<IssuedCertificateDTO>>();
            body.Data.Should().NotBeNull();
            return body.Data!;
        }

        [Fact]
        public async Task Issue_WhenUnauthorized_ShouldReturn401()
        {
            var seed = SeedIssueData();
            var payload = new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } }
            };

            var res = await _client.PostAsJsonAsync("/api/certificates/issue", payload);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Issue_WhenTemplateNotFound_ShouldReturn404_TEMPLATE_NOT_FOUND()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var payload = new IssueCertificatesDTO
            {
                TemplateId = Guid.NewGuid(),
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(CertificateErrorCodeConstants.TemplateNotFound);
        }

        [Fact]
        public async Task Issue_WhenRecipientInvalid_ShouldReturn400_RECIPIENT_INVALID()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var payload = new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO>
                {
                    new IssueRecipientDTO { TeamId = seed.TeamId, StudentId = seed.StudentId }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("RECIPIENT_INVALID");
        }

        [Fact]
        public async Task Issue_WhenTeamNotInContest_ShouldReturn400_BADREQUEST()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var payload = new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.OtherTeamId } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task Issue_WhenStudentNotInContest_ShouldReturn400_BADREQUEST()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var payload = new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { StudentId = seed.OtherStudentId } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task Issue_WhenOrganizerNotOwner_ShouldReturn403_FORBIDDEN()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OtherOrganizerEmail, seed.OtherOrganizerPassword);

            var payload = new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.FORBIDDEN);
        }

        [Fact]
        public async Task Issue_WhenDuplicateAndReissueFalse_ShouldReturn409_DUPLICATE_CERTIFICATE()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            await IssueAsync(token, new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } },
                Reissue = false
            });

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificates/issue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } },
                Reissue = false
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(CertificateErrorCodeConstants.DuplicateCertificate);
        }

        [Fact]
        public async Task Issue_WhenOutputMissing_ShouldStillSucceed()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            var payload = new
            {
                TemplateId = seed.TemplateId,
                Recipients = new[]
                {
                    new { TeamId = seed.TeamId }
                },
                Reissue = false
            };

            var issued = await IssueAsync(token, payload);
            issued.Count.Should().Be(1);
        }

        [Fact]
        public async Task Issue_WhenReissueTrue_ShouldWriteActivityLog()
        {
            var seed = SeedIssueData();
            var token = await LoginAsync(seed.OrganizerEmail, seed.OrganizerPassword);

            await IssueAsync(token, new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } },
                Reissue = false
            });

            var reissued = await IssueAsync(token, new IssueCertificatesDTO
            {
                TemplateId = seed.TemplateId,
                Recipients = new List<IssueRecipientDTO> { new IssueRecipientDTO { TeamId = seed.TeamId } },
                Reissue = true
            });

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            db.ActivityLogs.Any(l => l.UserId == seed.OrganizerUserId
                                     && l.Action == ActivityActions.CertificateReissue
                                     && l.TargetType == TargetTypes.Certificate
                                     && l.TargetId == reissued[0].CertificateId.ToString()).Should().BeTrue();
        }
    }
}
