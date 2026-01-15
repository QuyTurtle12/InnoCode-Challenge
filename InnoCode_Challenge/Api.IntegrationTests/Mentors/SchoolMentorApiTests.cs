using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.MentorManagementDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Xunit;

namespace Api.IntegrationTests.Mentors
{
    public class SchoolMentorApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public SchoolMentorApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<string> LoginSchoolManagerAsync()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = TestSeed.SchoolManagerEmail,
                Password = TestSeed.SchoolManagerPassword
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<AuthResponseDTO>();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private async Task<string> LoginAdminAsync()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = TestSeed.AdminEmail,
                Password = TestSeed.AdminPassword
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<AuthResponseDTO>();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private static MentorManagerRequestDTO BuildMentorDto(string email, string password, string? phone = null)
        {
            return new MentorManagerRequestDTO
            {
                Fullname = "Mentor Test",
                Email = email,
                Password = password,
                ConfirmPassword = password,
                Phone = phone
            };
        }

        [Fact]
        public async Task Create_WhenValid_ShouldCreateMentorAndUser()
        {
            var token = await LoginSchoolManagerAsync();
            var email = $"mentor{Guid.NewGuid():N}@test.com";

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/schools/{TestSeed.SchoolId:D}/mentors");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildMentorDto(email, "P@ssword123!", "0123456789"));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await res.ReadOkAsync<MentorProfileDTO>();
            body.Data!.Email.Should().Be(email.ToLowerInvariant());
            body.Data!.Role.Should().Be(RoleConstants.Mentor);
            body.Data!.SchoolId.Should().Be(TestSeed.SchoolId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var user = db.Users.First(u => u.Email == email.ToLowerInvariant());
            user.Role.Should().Be(RoleConstants.Mentor);

            var mentor = db.Mentors.First(m => m.UserId == user.UserId);
            mentor.SchoolId.Should().Be(TestSeed.SchoolId);
            mentor.CreatedBy.Should().Be(db.Users.First(u => u.Email == TestSeed.SchoolManagerEmail.ToLower()).UserId);
            mentor.Phone.Should().Be("0123456789");
        }

        [Fact]
        public async Task Create_WhenUnauthorized_ShouldReturn401()
        {
            var email = $"mentor{Guid.NewGuid():N}@test.com";

            var res = await _client.PostAsJsonAsync($"/api/schools/{TestSeed.SchoolId:D}/mentors",
                BuildMentorDto(email, "P@ssword123!"));

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Create_WhenNotSchoolManager_ShouldReturn403()
        {
            var adminToken = await LoginAdminAsync();
            var email = $"mentor{Guid.NewGuid():N}@test.com";

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/schools/{TestSeed.SchoolId:D}/mentors");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonContent.Create(BuildMentorDto(email, "P@ssword123!"));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Create_WhenPasswordMismatch_ShouldReturn400_VALIDATION_ERROR()
        {
            var token = await LoginSchoolManagerAsync();
            var email = $"mentor{Guid.NewGuid():N}@test.com";

            var dto = BuildMentorDto(email, "P@ssword123!");
            dto.ConfirmPassword = "Different123!";

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/schools/{TestSeed.SchoolId:D}/mentors");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(dto);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
        }

        [Fact]
        public async Task Create_WhenEmailExists_ShouldReturn409_EMAIL_EXISTS()
        {
            var token = await LoginSchoolManagerAsync();

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/schools/{TestSeed.SchoolId:D}/mentors");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildMentorDto(TestSeed.AdminEmail, "P@ssword123!"));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_EXISTS");
        }

        [Fact]
        public async Task Create_WhenSchoolNotFound_ShouldReturn404_SCHOOL_NOT_FOUND()
        {
            var token = await LoginSchoolManagerAsync();
            var email = $"mentor{Guid.NewGuid():N}@test.com";

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/schools/{Guid.NewGuid():D}/mentors");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildMentorDto(email, "P@ssword123!"));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("SCHOOL_NOT_FOUND");
        }

        [Fact]
        public async Task Create_WhenNotManagerOfSchool_ShouldReturn403()
        {
            Guid schoolId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
                var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLower());

                var school = new School
                {
                    SchoolId = Guid.NewGuid(),
                    Name = "Other School",
                    ProvinceId = TestSeed.DefaultProvinceId,
                    ManagerUserId = admin.UserId,
                    CreatedAt = DateTime.UtcNow
                };

                db.Schools.Add(school);
                db.SaveChanges();
                schoolId = school.SchoolId;
            }

            var token = await LoginSchoolManagerAsync();
            var email = $"mentor{Guid.NewGuid():N}@test.com";

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/schools/{schoolId:D}/mentors");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(BuildMentorDto(email, "P@ssword123!"));

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
