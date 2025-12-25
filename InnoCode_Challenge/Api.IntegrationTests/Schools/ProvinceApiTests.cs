using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.ProvinceDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Schools
{
    public class ProvinceApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public ProvinceApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private sealed record UserSeed(Guid UserId, string Email, string Password);

        private UserSeed SeedStaffUser()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var email = $"staff{Guid.NewGuid():N}@test.com";
            const string password = "P@ssword123!";

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Test Staff",
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = RoleConstants.Staff,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Users.Add(user);
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

        private Province SeedProvince(string name, string? address = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var province = new Province
            {
                ProvinceId = Guid.NewGuid(),
                Name = name,
                Address = address
            };

            db.Provinces.Add(province);
            db.SaveChanges();

            return province;
        }

        private School SeedSchool(Guid provinceId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var school = new School
            {
                SchoolId = Guid.NewGuid(),
                Name = "Test School",
                ProvinceId = provinceId,
                CreatedAt = now
            };

            db.Schools.Add(school);
            db.SaveChanges();

            return school;
        }

        private SchoolCreationRequest SeedSchoolCreationRequest(Guid provinceId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var requester = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = "Requester",
                Email = $"requester{Guid.NewGuid():N}@test.com",
                PasswordHash = PasswordHasher.Hash("P@ssword123!"),
                Role = RoleConstants.SchoolManager,
                Status = UserStatusConstants.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            var request = new SchoolCreationRequest
            {
                RequestId = Guid.NewGuid(),
                RequestedByUserId = requester.UserId,
                Name = "School Request",
                ProvinceId = provinceId,
                Status = SchoolCreationRequestStatus.Pending,
                CreatedAt = now
            };

            db.Users.Add(requester);
            db.SchoolCreationRequests.Add(request);
            db.SaveChanges();

            return request;
        }

        private MentorRegistration SeedMentorRegistration(Guid provinceId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var reg = new MentorRegistration
            {
                RegistrationId = Guid.NewGuid(),
                Fullname = "Mentor Register",
                Email = $"mentor{Guid.NewGuid():N}@test.com",
                ProvinceId = provinceId,
                Status = "PENDING",
                CreatedAt = now
            };

            db.MentorRegistrations.Add(reg);
            db.SaveChanges();

            return reg;
        }

        [Fact]
        public async Task Create_WhenNameExists_ShouldReturn400_NAME_EXISTS()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/provinces");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateProvinceDTO
            {
                Name = "Alpha Province"
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/provinces");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2.Content = JsonContent.Create(new CreateProvinceDTO
            {
                Name = "alpha province"
            });

            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res2.ReadErrorAsync();
            err.ErrorCode.Should().Be("NAME_EXISTS");
        }

        [Fact]
        public async Task Update_WhenNameExists_ShouldReturn400_NAME_EXISTS()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);

            var first = SeedProvince("Alpha");
            var second = SeedProvince("Beta");

            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/provinces/{second.ProvinceId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new UpdateProvinceDTO
            {
                Name = "alpha"
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("NAME_EXISTS");
        }

        [Fact]
        public async Task Delete_WhenProvinceHasSchool_ShouldReturn409_PROVINCE_IN_USE()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);

            var province = SeedProvince("InUseSchool");
            SeedSchool(province.ProvinceId);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/provinces/{province.ProvinceId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("PROVINCE_IN_USE");
        }

        [Fact]
        public async Task Delete_WhenProvinceHasSchoolCreationRequest_ShouldReturn409_PROVINCE_IN_USE()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);

            var province = SeedProvince("InUseRequest");
            SeedSchoolCreationRequest(province.ProvinceId);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/provinces/{province.ProvinceId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("PROVINCE_IN_USE");
        }

        [Fact]
        public async Task Delete_WhenProvinceHasMentorRegistration_ShouldReturn409_PROVINCE_IN_USE()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);

            var province = SeedProvince("InUseMentor");
            SeedMentorRegistration(province.ProvinceId);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/provinces/{province.ProvinceId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("PROVINCE_IN_USE");
        }

        [Fact]
        public async Task GetById_WhenNotFound_ShouldReturn404_PROVINCE_NOT_FOUND()
        {
            var res = await _client.GetAsync($"/api/provinces/{Guid.NewGuid():D}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("PROVINCE_NOT_FOUND");
        }
    }
}
