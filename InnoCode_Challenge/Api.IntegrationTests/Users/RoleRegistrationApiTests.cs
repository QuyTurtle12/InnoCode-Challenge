using Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.RoleRegistrationDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Xunit;

namespace Api.IntegrationTests.Users
{
    public class RoleRegistrationApiTests : IClassFixture<ApiFactory>
    {
        private readonly HttpClient _client;

        public RoleRegistrationApiTests(ApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        private static string NewEmail(string prefix) => $"{prefix}{Guid.NewGuid():N}@test.com";

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

        private async Task RegisterStaffAsync(string email, string password, string adminToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register-staff");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonContent.Create(new RegisterUserDTO
            {
                FullName = "Role Staff",
                Email = email,
                Password = password,
                ConfirmPassword = password
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        private static MultipartFormDataContent BuildSubmitForm(
            string requestedRole,
            string fullName,
            string email,
            string password,
            string confirmPassword,
            byte[]? evidenceBytes = null,
            string evidenceFileName = "evidence.pdf",
            string evidenceContentType = "application/pdf")
        {
            var content = new MultipartFormDataContent();

            content.Add(new StringContent(requestedRole), "RequestedRole");
            content.Add(new StringContent(fullName), "FullName");
            content.Add(new StringContent(email), "Email");
            content.Add(new StringContent(password), "Password");
            content.Add(new StringContent(confirmPassword), "ConfirmPassword");

            if (evidenceBytes != null)
            {
                var fileContent = new ByteArrayContent(evidenceBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(evidenceContentType);
                content.Add(fileContent, "EvidenceFiles", evidenceFileName);
            }

            return content;
        }

        private async Task<RoleRegistrationSubmittedDTO> SubmitAsync(
            string role,
            string email,
            string password,
            string? fullName = null)
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            using var form = BuildSubmitForm(
                requestedRole: role,
                fullName: fullName ?? "Role Registrant",
                email: email,
                password: password,
                confirmPassword: password,
                evidenceBytes: bytes);

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await res.ReadOkAsync<RoleRegistrationSubmittedDTO>();
            body.Data.Should().NotBeNull();
            return body.Data!;
        }

        [Fact]
        public async Task Submit_WhenValid_ShouldReturn201Pending()
        {
            var email = NewEmail("role");
            var password = "P@ssword123!";

            var submitted = await SubmitAsync(RoleConstants.Staff.ToLowerInvariant(), email, password);

            submitted.RegistrationId.Should().NotBe(Guid.Empty);
            submitted.Status.Should().Be(RoleRegistrationStatusConstants.Pending);
        }

        [Fact]
        public async Task Submit_WhenNoEvidence_ShouldReturn400_EVIDENCE_REQUIRED()
        {
            var email = NewEmail("noevidence");
            using var form = BuildSubmitForm(
                requestedRole: "staff",
                fullName: "Role Registrant",
                email: email,
                password: "P@ssword123!",
                confirmPassword: "P@ssword123!",
                evidenceBytes: null);

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EVIDENCE_REQUIRED");
        }

        [Fact]
        public async Task Submit_WhenInvalidFileType_ShouldReturn400_BADREQUEST()
        {
            var email = NewEmail("badfile");
            using var form = BuildSubmitForm(
                requestedRole: "staff",
                fullName: "Role Registrant",
                email: email,
                password: "P@ssword123!",
                confirmPassword: "P@ssword123!",
                evidenceBytes: new byte[] { 1, 2, 3 },
                evidenceFileName: "evidence.txt",
                evidenceContentType: "text/plain");

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
            err.ErrorMessage.Should().Contain("Evidence must be");
        }

        [Fact]
        public async Task Submit_WhenPasswordMismatch_ShouldReturn400_CONFIRM_PASSWORD_MISMATCH()
        {
            var email = NewEmail("mismatch");
            using var form = BuildSubmitForm(
                requestedRole: "staff",
                fullName: "Role Registrant",
                email: email,
                password: "P@ssword123!",
                confirmPassword: "Different123!",
                evidenceBytes: new byte[] { 1 });

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("CONFIRM_PASSWORD_MISMATCH");
        }

        [Fact]
        public async Task Submit_WhenInvalidRole_ShouldReturn400_INVALID_ROLE()
        {
            var email = NewEmail("invalidrole");
            using var form = BuildSubmitForm(
                requestedRole: "invalid",
                fullName: "Role Registrant",
                email: email,
                password: "P@ssword123!",
                confirmPassword: "P@ssword123!",
                evidenceBytes: new byte[] { 1 });

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_ROLE");
        }

        [Fact]
        public async Task Submit_WhenRoleSchoolManager_ShouldReturn201Pending()
        {
            var email = NewEmail("schoolmanager");
            var password = "P@ssword123!";

            var submitted = await SubmitAsync("schoolmanager", email, password);

            submitted.RegistrationId.Should().NotBe(Guid.Empty);
            submitted.Status.Should().Be(RoleRegistrationStatusConstants.Pending);
        }

        [Fact]
        public async Task Submit_WhenRoleAdmin_ShouldReturn201Pending()
        {
            var email = NewEmail("adminrole");
            var password = "P@ssword123!";

            var submitted = await SubmitAsync("admin", email, password);

            submitted.RegistrationId.Should().NotBe(Guid.Empty);
            submitted.Status.Should().Be(RoleRegistrationStatusConstants.Pending);
        }

        [Fact]
        public async Task Submit_WhenRoleStudent_ShouldReturn400_INVALID_ROLE()
        {
            var email = NewEmail("studentrole");
            using var form = BuildSubmitForm(
                requestedRole: "student",
                fullName: "Role Registrant",
                email: email,
                password: "P@ssword123!",
                confirmPassword: "P@ssword123!",
                evidenceBytes: new byte[] { 1 });

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_ROLE");
        }

        [Fact]
        public async Task Submit_WhenEmailAlreadyRegistered_ShouldReturn409_EMAIL_EXISTS()
        {
            using var form = BuildSubmitForm(
                requestedRole: "staff",
                fullName: "Role Registrant",
                email: TestSeed.AdminEmail,
                password: "P@ssword123!",
                confirmPassword: "P@ssword123!",
                evidenceBytes: new byte[] { 1 });

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_EXISTS");
        }

        [Fact]
        public async Task Submit_WhenPendingExists_ShouldReturn409_REGISTRATION_EXISTS()
        {
            var email = NewEmail("pending");
            var password = "P@ssword123!";

            await SubmitAsync("staff", email, password);

            using var form = BuildSubmitForm(
                requestedRole: "staff",
                fullName: "Role Registrant",
                email: email,
                password: password,
                confirmPassword: password,
                evidenceBytes: new byte[] { 9, 9, 9 });

            var res = await _client.PostAsync("/api/role-registrations", form);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("REGISTRATION_EXISTS");
        }

        [Fact]
        public async Task List_WhenUnauthorized_ShouldReturn401()
        {
            var res = await _client.GetAsync("/api/role-registrations");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task List_WhenInvalidPage_ShouldReturn400_BADREQUEST()
        {
            var token = await LoginAdminAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/role-registrations?page=0&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be(ResponseCodeConstants.BADREQUEST);
        }

        [Fact]
        public async Task List_WhenAdmin_ShouldReturnItems()
        {
            var email = NewEmail("list");
            await SubmitAsync("staff", email, "P@ssword123!");

            var token = await LoginAdminAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/role-registrations?page=1&pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<List<RoleRegistrationDTO>>();
            body.Data.Should().NotBeNull();
            body.Data!.Count.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task GetById_WhenNotFound_ShouldReturn404_ROLE_REG_NOT_FOUND()
        {
            var token = await LoginAdminAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/role-registrations/{Guid.NewGuid():D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("ROLE_REG_NOT_FOUND");
        }

        [Fact]
        public async Task Approve_WhenPending_ShouldCreateUserAndUpdateStatus()
        {
            var email = NewEmail("approve");
            var password = "P@ssword123!";

            var submitted = await SubmitAsync("staff", email, password);

            var token = await LoginAdminAsync();
            var approveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/approve");
            approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var approveRes = await _client.SendAsync(approveReq);
            approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/role-registrations/{submitted.RegistrationId:D}");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getRes = await _client.SendAsync(getReq);
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var detail = await getRes.ReadOkAsync<RoleRegistrationDetailDTO>();
            detail.Data!.Status.Should().Be(RoleRegistrationStatusConstants.Approved);
            detail.Data!.ReviewedBy.Should().NotBeNull();

            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = email,
                Password = password
            });

            loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var loginBody = await loginRes.ReadOkAsync<AuthResponseDTO>();
            loginBody.Data!.Role.Should().Be(RoleConstants.Staff);
        }

        [Fact]
        public async Task Approve_WhenNotPending_ShouldReturn409_NOT_PENDING()
        {
            var email = NewEmail("approvenotp");
            var password = "P@ssword123!";
            var submitted = await SubmitAsync("staff", email, password);

            var token = await LoginAdminAsync();
            var firstReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/approve");
            firstReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var firstApprove = await _client.SendAsync(firstReq);
            firstApprove.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/approve");
            secondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var secondApprove = await _client.SendAsync(secondReq);
            secondApprove.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await secondApprove.ReadErrorAsync();
            err.ErrorCode.Should().Be("NOT_PENDING");
        }

        [Fact]
        public async Task Approve_WhenEmailAlreadyExists_ShouldReturn409_EMAIL_EXISTS()
        {
            var email = NewEmail("approveexists");
            var password = "P@ssword123!";
            var submitted = await SubmitAsync("staff", email, password);

            var token = await LoginAdminAsync();
            await RegisterStaffAsync(email, password, token);

            var approveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/approve");
            approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(approveReq);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_EXISTS");
        }

        [Fact]
        public async Task Deny_WhenPending_ShouldUpdateStatusAndReason()
        {
            var email = NewEmail("deny");
            var password = "P@ssword123!";

            var submitted = await SubmitAsync("judge", email, password);

            var token = await LoginAdminAsync();
            var denyReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/deny");
            denyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            denyReq.Content = JsonContent.Create(new DenyRoleRegistrationDTO { Reason = "Not eligible" });

            var denyRes = await _client.SendAsync(denyReq);
            denyRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/role-registrations/{submitted.RegistrationId:D}");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getRes = await _client.SendAsync(getReq);
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var detail = await getRes.ReadOkAsync<RoleRegistrationDetailDTO>();
            detail.Data!.Status.Should().Be(RoleRegistrationStatusConstants.Denied);
            detail.Data!.DenyReason.Should().Be("Not eligible");
        }

        [Fact]
        public async Task Deny_WhenReasonEmpty_ShouldReturn400_REASON_REQUIRED()
        {
            var email = NewEmail("denyempty");
            var password = "P@ssword123!";
            var submitted = await SubmitAsync("judge", email, password);

            var token = await LoginAdminAsync();
            var denyReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/deny");
            denyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            denyReq.Content = JsonContent.Create(new DenyRoleRegistrationDTO { Reason = "   " });

            var denyRes = await _client.SendAsync(denyReq);
            denyRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await denyRes.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
        }

        [Fact]
        public async Task Deny_WhenNotPending_ShouldReturn409_NOT_PENDING()
        {
            var email = NewEmail("denynotp");
            var password = "P@ssword123!";
            var submitted = await SubmitAsync("judge", email, password);

            var token = await LoginAdminAsync();
            var firstReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/deny");
            firstReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            firstReq.Content = JsonContent.Create(new DenyRoleRegistrationDTO { Reason = "Not eligible" });
            var firstDeny = await _client.SendAsync(firstReq);
            firstDeny.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondReq = new HttpRequestMessage(HttpMethod.Post, $"/api/role-registrations/{submitted.RegistrationId:D}/deny");
            secondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            secondReq.Content = JsonContent.Create(new DenyRoleRegistrationDTO { Reason = "Not eligible" });
            var secondDeny = await _client.SendAsync(secondReq);
            secondDeny.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var err = await secondDeny.ReadErrorAsync();
            err.ErrorCode.Should().Be("NOT_PENDING");
        }
    }
}
