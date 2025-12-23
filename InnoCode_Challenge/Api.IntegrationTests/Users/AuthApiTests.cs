using Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using Repository.DTOs.AuthDTOs;
using System.Net;
using System.Net.Http.Json;
using Utility.Constant;

namespace Api.IntegrationTests.Users
{
    public class AuthApiTests : IClassFixture<ApiFactory>
    {
        private readonly HttpClient _client;

        public AuthApiTests(ApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        private static string NewEmail(string prefix) => $"{prefix}{Guid.NewGuid():N}@test.com";

        [Fact]
        public async Task Register_WhenPasswordTooShort_ShouldReturn400_ValidationError()
        {
            var dto = new RegisterStudentDTO
            {
                FullName = "Student A",
                Email = NewEmail("short"),
                Password = "123456",
                ConfirmPassword = "123456",
                SchoolId = TestSeed.SchoolId
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
            err.ErrorMessage.Should().Contain("minimum length").And.Contain("8");
        }

        [Fact]
        public async Task Register_WhenConfirmPasswordMismatch_ShouldReturn400_ValidationError()
        {
            var dto = new RegisterStudentDTO
            {
                FullName = "Student A",
                Email = NewEmail("mismatch"),
                Password = "P@ssword123!",
                ConfirmPassword = "Different123!",
                SchoolId = TestSeed.SchoolId
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
            err.ErrorMessage.Should().Contain("ConfirmPassword");
            err.ErrorMessage.Should().Contain("Passwords do not match");
        }

        [Fact]
        public async Task Register_Success_ShouldReturn201_WithBaseResponseModel()
        {
            var email = NewEmail("ok");

            var dto = new RegisterStudentDTO
            {
                FullName = "Student B",
                Email = email,
                Password = "P@ssword123!",
                ConfirmPassword = "P@ssword123!",
                SchoolId = TestSeed.SchoolId
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);

            if (res.StatusCode != HttpStatusCode.Created)
            {
                var raw = await res.Content.ReadAsStringAsync();
                throw new Exception($"Expected 201 but got {(int)res.StatusCode}. Body: {raw}");
            }

            var body = await res.ReadOkAsync<AuthResponseDTO>();
            body.StatusCode.Should().Be(201);
            body.Code.Should().Be(ResponseCodeConstants.SUCCESS);
            body.Data.Should().NotBeNull();

            body.Data!.Email.Should().Be(email.ToLowerInvariant()); // NormalizeEmail()
            body.Data!.EmailVerified.Should().BeFalse();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            body.Data!.RefreshToken.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Login_WhenInvalidCredentials_ShouldReturn401_WithErrorEnvelope()
        {
            var dto = new LoginDTO
            {
                Email = NewEmail("nope"),
                Password = "WrongPass123!"
            };

            var res = await _client.PostAsJsonAsync("/api/auth/login", dto);
            var raw = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_CREDENTIALS");
            err.ErrorMessage.Should().Contain("incorrect");
        }
    }
}
