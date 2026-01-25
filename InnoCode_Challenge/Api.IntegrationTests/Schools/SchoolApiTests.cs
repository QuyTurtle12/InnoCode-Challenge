using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.SchoolDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Helpers;
using Xunit;

namespace Api.IntegrationTests.Schools
{
    public class SchoolApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public SchoolApiTests(ApiFactory factory)
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

        private Province EnsureProvince()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var province = db.Provinces.First(p => p.ProvinceId == TestSeed.DefaultProvinceId);
            return province;
        }

        private School SeedSchool(string name, Guid provinceId, Guid? managerUserId = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var school = new School
            {
                SchoolId = Guid.NewGuid(),
                Name = name,
                ProvinceId = provinceId,
                ManagerUserId = managerUserId,
                CreatedAt = now
            };

            db.Schools.Add(school);
            db.SaveChanges();

            return school;
        }

        private Student SeedStudent(Guid schoolId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var user = new User
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
                UserId = user.UserId,
                SchoolId = schoolId,
                CreatedAt = now
            };

            db.Users.Add(user);
            db.Students.Add(student);
            db.SaveChanges();

            return student;
        }

        private SchoolCreationRequest SeedSchoolCreationRequest(Guid provinceId, Guid createdSchoolId)
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
                Status = SchoolCreationRequestStatus.Approved,
                CreatedSchoolId = createdSchoolId,
                CreatedAt = now
            };

            db.Users.Add(requester);
            db.SchoolCreationRequests.Add(request);
            db.SaveChanges();

            return request;
        }

        [Fact]
        public async Task GetById_WhenNotFound_ShouldReturn404_SCHOOL_NOT_FOUND()
        {
            var res = await _client.GetAsync($"/api/schools/{Guid.NewGuid():D}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("SCHOOL_NOT_FOUND");
        }

        [Fact]
        public async Task Create_WhenNameExistsInProvince_ShouldReturn400_NAME_EXISTS()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);
            var province = EnsureProvince();

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/schools");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new CreateSchoolDTO
            {
                Name = "Alpha School",
                ProvinceId = province.ProvinceId
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/schools");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2.Content = JsonContent.Create(new CreateSchoolDTO
            {
                Name = "alpha school",
                ProvinceId = province.ProvinceId
            });

            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res2.ReadErrorAsync();
            err.ErrorCode.Should().Be("NAME_EXISTS");
        }

        [Fact]
        public async Task Update_WhenProvinceNotFound_ShouldReturn404_PROVINCE_NOT_FOUND()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);
            var province = EnsureProvince();
            var school = SeedSchool("Update School", province.ProvinceId);

            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/schools/{school.SchoolId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new UpdateSchoolDTO
            {
                ProvinceId = Guid.NewGuid()
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("PROVINCE_NOT_FOUND");
        }

        [Fact]
        public async Task Update_ShouldAllowEditingAddress()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);
            var province = EnsureProvince();
            var school = SeedSchool("Address School", province.ProvinceId);

            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/schools/{school.SchoolId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new UpdateSchoolDTO
            {
                Address = " 123 Main Street "
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<SchoolDTO>();
            body.Data.Should().NotBeNull();
            body.Data!.Address.Should().Be("123 Main Street");
        }

        [Fact]
        public async Task Delete_WhenSchoolHasStudents_ShouldReturn409_SCHOOL_IN_USE()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);
            var province = EnsureProvince();
            var school = SeedSchool("Student School", province.ProvinceId);
            SeedStudent(school.SchoolId);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/schools/{school.SchoolId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("SCHOOL_IN_USE");
        }

        [Fact]
        public async Task Delete_WhenSchoolHasSchoolCreationRequest_ShouldReturn409_SCHOOL_IN_USE()
        {
            var staff = SeedStaffUser();
            var token = await LoginAsync(staff.Email, staff.Password);
            var province = EnsureProvince();
            var school = SeedSchool("Request School", province.ProvinceId);
            SeedSchoolCreationRequest(province.ProvinceId, school.SchoolId);

            var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/schools/{school.SchoolId:D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("SCHOOL_IN_USE");
        }

        [Fact]
        public async Task GetMyManagedSchools_ShouldReturnOnlyMine()
        {
            var token = await LoginAsync(TestSeed.SchoolManagerEmail, TestSeed.SchoolManagerPassword);

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/schools/my-managed?page=1&pageSize=20");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<List<SchoolDTO>>();
            body.Data.Should().NotBeNull();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var manager = db.Users.First(u => u.Email == TestSeed.SchoolManagerEmail.ToLowerInvariant());

            body.Data!.All(x => x.ManagerUserId == manager.UserId).Should().BeTrue();
        }
    }
}
