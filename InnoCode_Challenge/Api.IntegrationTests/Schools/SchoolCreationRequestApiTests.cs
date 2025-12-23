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
using Xunit;

namespace Api.IntegrationTests.Schools
{
    public class SchoolCreationRequestApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public SchoolCreationRequestApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
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

        private async Task<string> CreateAndLoginSchoolManagerAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            if (!db.Users.Any(u => u.Email == email.ToLowerInvariant()))
            {
                db.Users.Add(new User
                {
                    UserId = Guid.NewGuid(),
                    Fullname = "Alt School Manager",
                    Email = email.ToLowerInvariant(),
                    PasswordHash = Utility.Helpers.PasswordHasher.Hash("P@ssword123!"),
                    Role = RoleConstants.SchoolManager,
                    Status = UserStatusConstants.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
                db.SaveChanges();
            }

            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = email,
                Password = "P@ssword123!"
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<AuthResponseDTO>();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private static MultipartFormDataContent BuildCreateForm(
            string name,
            Guid provinceId,
            byte[]? evidenceBytes = null,
            string evidenceFileName = "evidence.pdf",
            string evidenceContentType = "application/pdf")
        {
            var content = new MultipartFormDataContent
            {
                { new StringContent(name), "Name" },
                { new StringContent(provinceId.ToString()), "ProvinceId" }
            };

            if (evidenceBytes != null)
            {
                var fileContent = new ByteArrayContent(evidenceBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(evidenceContentType);
                content.Add(fileContent, "Evidences", evidenceFileName);
            }

            return content;
        }

        private async Task<SchoolCreationRequestDetailDTO> CreateRequestAsync(string token)
        {
            using var form = BuildCreateForm(
                name: "Test School Request",
                provinceId: TestSeed.DefaultProvinceId,
                evidenceBytes: new byte[] { 1, 2, 3 },
                evidenceFileName: "evidence.pdf",
                evidenceContentType: "application/pdf");

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/school-creation-requests");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await res.ReadOkAsync<SchoolCreationRequestDetailDTO>();
            body.Data.Should().NotBeNull();
            return body.Data!;
        }

        [Fact]
        public async Task Create_WhenSchoolManager_ShouldReturn201_AndNotifyStaffAdmin_AndLog()
        {
            var token = await LoginSchoolManagerAsync();
            var created = await CreateRequestAsync(token);

            created.RequestId.Should().NotBe(Guid.Empty);
            created.Status.Should().Be(SchoolCreationRequestStatus.Pending);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLower());
            db.Notifications.Any(n => n.UserId == admin.UserId &&
                                      n.Type == NotificationTypes.SchoolCreationRequestSubmitted).Should().BeTrue();

            db.ActivityLogs.Any(l => l.Action == ActivityActions.SchoolRequestCreate &&
                                     l.TargetType == TargetTypes.SchoolCreationRequest &&
                                     l.TargetId == created.RequestId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task Create_WhenNotAuthenticated_ShouldReturn401()
        {
            using var form = BuildCreateForm(
                name: "Test School Request",
                provinceId: TestSeed.DefaultProvinceId,
                evidenceBytes: new byte[] { 1, 2, 3 });

            var res = await _client.PostAsync("/api/school-creation-requests", form);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Create_WhenNotSchoolManager_ShouldReturn403()
        {
            var adminToken = await LoginAdminAsync();
            using var form = BuildCreateForm(
                name: "Test School Request",
                provinceId: TestSeed.DefaultProvinceId,
                evidenceBytes: new byte[] { 1, 2, 3 });

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/school-creation-requests");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Create_WhenMissingEvidence_ShouldReturn400_EVIDENCE_REQUIRED()
        {
            var token = await LoginSchoolManagerAsync();
            using var form = BuildCreateForm(
                name: "Test School Request",
                provinceId: TestSeed.DefaultProvinceId,
                evidenceBytes: null);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/school-creation-requests");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EVIDENCE_REQUIRED");
        }

        [Fact]
        public async Task Create_WhenInvalidEvidenceType_ShouldReturn400_INVALID_EVIDENCE_TYPE()
        {
            var token = await LoginSchoolManagerAsync();
            using var form = BuildCreateForm(
                name: "Test School Request",
                provinceId: TestSeed.DefaultProvinceId,
                evidenceBytes: new byte[] { 1, 2, 3 },
                evidenceFileName: "evidence.txt",
                evidenceContentType: "text/plain");

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/school-creation-requests");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_EVIDENCE_TYPE");
        }

        [Fact]
        public async Task Create_WhenProvinceNotFound_ShouldReturn404_PROVINCE_NOT_FOUND()
        {
            var token = await LoginSchoolManagerAsync();
            using var form = BuildCreateForm(
                name: "Test School Request",
                provinceId: Guid.NewGuid(),
                evidenceBytes: new byte[] { 1, 2, 3 },
                evidenceFileName: "evidence.docx",
                evidenceContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/school-creation-requests");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = form;

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("PROVINCE_NOT_FOUND");
        }

        [Fact]
        public async Task List_WhenPageNumberInvalid_ShouldReturn400_BADREQUEST()
        {
            var token = await LoginAdminAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/school-creation-requests?pageNumber=0&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task My_WhenPageNumberInvalid_ShouldReturn400_BADREQUEST()
        {
            var token = await LoginSchoolManagerAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/school-creation-requests/my?pageNumber=0&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task My_ShouldReturnOnlyRequesterItems()
        {
            var tokenA = await LoginSchoolManagerAsync();
            var createdA = await CreateRequestAsync(tokenA);

            var emailB = $"sm{Guid.NewGuid():N}@test.com";
            var tokenB = await CreateAndLoginSchoolManagerAsync(emailB);
            await CreateRequestAsync(tokenB);

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/school-creation-requests/my?pageNumber=1&pageSize=20");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<List<SchoolCreationRequestListDTO>>();
            body.Data.Should().NotBeNull();
            body.Data!.Any(x => x.RequestId == createdA.RequestId).Should().BeTrue();
            body.Data!.All(x => x.RequestedByEmail == TestSeed.SchoolManagerEmail.ToLowerInvariant()).Should().BeTrue();
        }

        [Fact]
        public async Task Approve_WhenPending_ShouldCreateSchool_AndNotifyRequester_AndLog()
        {
            var schoolManagerToken = await LoginSchoolManagerAsync();
            var created = await CreateRequestAsync(schoolManagerToken);

            var adminToken = await LoginAdminAsync();
            var approveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/school-creation-requests/{created.RequestId:D}/approve");
            approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var approveRes = await _client.SendAsync(approveReq);
            approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var reqEntity = db.SchoolCreationRequests.First(x => x.RequestId == created.RequestId);
            reqEntity.Status.Should().Be(SchoolCreationRequestStatus.Approved);
            reqEntity.CreatedSchoolId.Should().NotBeNull();

            db.Schools.Any(s => s.SchoolId == reqEntity.CreatedSchoolId &&
                                s.ManagerUserId == reqEntity.RequestedByUserId).Should().BeTrue();

            db.ActivityLogs.Any(l => l.Action == ActivityActions.SchoolRequestApprove &&
                                     l.TargetType == TargetTypes.SchoolCreationRequest &&
                                     l.TargetId == created.RequestId.ToString()).Should().BeTrue();

            var requester = db.Users.First(u => u.UserId == reqEntity.RequestedByUserId);
            db.Notifications.Any(n => n.UserId == requester.UserId &&
                                      n.Type == NotificationTypes.SchoolCreationRequestApproved).Should().BeTrue();
        }

        [Fact]
        public async Task Approve_WhenNotPending_ShouldReturn409_NOT_PENDING()
        {
            var schoolManagerToken = await LoginSchoolManagerAsync();
            var created = await CreateRequestAsync(schoolManagerToken);

            var adminToken = await LoginAdminAsync();
            var approveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/school-creation-requests/{created.RequestId:D}/approve");
            approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var first = await _client.SendAsync(approveReq);
            first.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondReq = new HttpRequestMessage(HttpMethod.Post, $"/api/school-creation-requests/{created.RequestId:D}/approve");
            secondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var second = await _client.SendAsync(secondReq);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await second.ReadErrorAsync();
            err.ErrorCode.Should().Be("NOT_PENDING");
        }

        [Fact]
        public async Task Deny_WhenPending_ShouldNotifyRequester_AndLog()
        {
            var schoolManagerToken = await LoginSchoolManagerAsync();
            var created = await CreateRequestAsync(schoolManagerToken);

            var adminToken = await LoginAdminAsync();
            var denyReq = new HttpRequestMessage(HttpMethod.Post, $"/api/school-creation-requests/{created.RequestId:D}/deny");
            denyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            denyReq.Content = JsonContent.Create(new DenySchoolCreationRequestDTO { DenyReason = "Insufficient info" });

            var denyRes = await _client.SendAsync(denyReq);
            denyRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var reqEntity = db.SchoolCreationRequests.First(x => x.RequestId == created.RequestId);
            reqEntity.Status.Should().Be(SchoolCreationRequestStatus.Denied);

            db.ActivityLogs.Any(l => l.Action == ActivityActions.SchoolRequestDeny &&
                                     l.TargetType == TargetTypes.SchoolCreationRequest &&
                                     l.TargetId == created.RequestId.ToString()).Should().BeTrue();

            var requester = db.Users.First(u => u.UserId == reqEntity.RequestedByUserId);
            db.Notifications.Any(n => n.UserId == requester.UserId &&
                                      n.Type == NotificationTypes.SchoolCreationRequestDenied).Should().BeTrue();
        }

        [Fact]
        public async Task Deny_WhenNotPending_ShouldReturn409_NOT_PENDING()
        {
            var schoolManagerToken = await LoginSchoolManagerAsync();
            var created = await CreateRequestAsync(schoolManagerToken);

            var adminToken = await LoginAdminAsync();
            var denyReq = new HttpRequestMessage(HttpMethod.Post, $"/api/school-creation-requests/{created.RequestId:D}/deny");
            denyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            denyReq.Content = JsonContent.Create(new DenySchoolCreationRequestDTO { DenyReason = "Reason" });

            var first = await _client.SendAsync(denyReq);
            first.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondReq = new HttpRequestMessage(HttpMethod.Post, $"/api/school-creation-requests/{created.RequestId:D}/deny");
            secondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            secondReq.Content = JsonContent.Create(new DenySchoolCreationRequestDTO { DenyReason = "Reason" });

            var second = await _client.SendAsync(secondReq);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await second.ReadErrorAsync();
            err.ErrorCode.Should().Be("NOT_PENDING");
        }
    }
}
