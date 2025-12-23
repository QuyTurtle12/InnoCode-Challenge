using Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using Repository.DTOs.AuthDTOs;
using System.Net;
using System.Net.Http.Headers;
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

        private async Task<TestBaseResponse<AuthResponseDTO>> RegisterStudentAsync(string email, string password)
        {
            var dto = new RegisterStudentDTO
            {
                FullName = "Student Test",
                Email = email,
                Password = password,
                ConfirmPassword = password,
                SchoolId = TestSeed.SchoolId
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            return await res.ReadOkAsync<AuthResponseDTO>();
        }

        private async Task<TestBaseResponse<AuthResponseDTO>> LoginOkAsync(string email, string password)
        {
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = email,
                Password = password
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            return await res.ReadOkAsync<AuthResponseDTO>();
        }

        private async Task<string> GenerateVerificationTokenAsync(string accessToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/generate-verification-token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<VerificationTokenDTO>();
            body.Data.Should().NotBeNull();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();

            return body.Data!.Token!;
        }

        private async Task VerifyEmailAsync(string token)
        {
            var res = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailDTO
            {
                Token = token
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        private async Task<TestBaseResponse<AuthResponseDTO>> RefreshOkAsync(string refreshToken)
        {
            var res = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDTO
            {
                RefreshToken = refreshToken
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            return await res.ReadOkAsync<AuthResponseDTO>();
        }

        private async Task<HttpResponseMessage> MeAsync(string accessToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await _client.SendAsync(req);
        }

        private async Task<(string Email, string Password)> CreateVerifiedStudentAsync()
        {
            var email = NewEmail("student");
            var password = "P@ssword123!";

            var reg = await RegisterStudentAsync(email, password);
            reg.Data!.EmailVerified.Should().BeFalse();
            var verifyToken = await GenerateVerificationTokenAsync(reg.Data.Token);

            await VerifyEmailAsync(verifyToken);

            var login = await LoginOkAsync(email, password);
            login.Data!.EmailVerified.Should().BeTrue();

            return (email, password);
        }

        [Fact]
        public async Task Register_WhenDuplicateEmail_ShouldReturn400_EMAIL_EXISTS()
        {
            var email = NewEmail("dup");
            var password = "P@ssword123!";

            await RegisterStudentAsync(email, password);

            var dto2 = new RegisterStudentDTO
            {
                FullName = "Student Dup",
                Email = email,
                Password = password,
                ConfirmPassword = password,
                SchoolId = TestSeed.SchoolId
            };

            var res2 = await _client.PostAsJsonAsync("/api/auth/register", dto2);
            res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res2.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_EXISTS");
            err.ErrorMessage.Should().Contain("already registered");
        }

        [Fact]
        public async Task Register_WhenSchoolNotFound_ShouldReturn404_SCHOOL_NOT_FOUND()
        {
            var dto = new RegisterStudentDTO
            {
                FullName = "Student X",
                Email = NewEmail("schoolnotfound"),
                Password = "P@ssword123!",
                ConfirmPassword = "P@ssword123!",
                SchoolId = Guid.NewGuid() // not existing
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("SCHOOL_NOT_FOUND");
            err.ErrorMessage.Should().Contain("No school");
        }
        [Fact]
        public async Task Register_WhenSchoolIdEmpty_ShouldReturn400_SCHOOL_ID_REQUIRED()
        {
            var dto = new RegisterStudentDTO
            {
                FullName = "Student A",
                Email = NewEmail("empty-school"),
                Password = "P@ssword123!",
                ConfirmPassword = "P@ssword123!",
                SchoolId = Guid.Empty
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("SCHOOL_ID_REQUIRED");
            err.ErrorMessage.Should().Contain("SchoolId");
        }

        [Fact]
        public async Task Register_WhenConfirmPasswordMismatch_ShouldReturn400_CONFIRM_PASSWORD_MISMATCH()
        {
            var dto = new RegisterStudentDTO
            {
                FullName = "Student A",
                Email = NewEmail("mismatch2"),
                Password = "P@ssword123!",
                ConfirmPassword = "Different123!",
                SchoolId = TestSeed.SchoolId
            };

            var res = await _client.PostAsJsonAsync("/api/auth/register", dto);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
            err.ErrorMessage.Should().Contain("do not match");
        }

        [Fact]
        public async Task Login_WhenUserUnverified_ShouldReturn403_USER_UNVERIFIED()
        {
            var email = NewEmail("unverified");
            var password = "P@ssword123!";

            await RegisterStudentAsync(email, password);

            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = email,
                Password = password
            });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("USER_UNVERIFIED");
            err.ErrorMessage.Should().Contain("verify");
        }
        [Fact]
        public async Task Login_WhenPasswordWrong_ShouldReturn401_INVALID_CREDENTIALS()
        {
            var email = NewEmail("login-wrong");
            var password = "P@ssword123!";

            var reg = await RegisterStudentAsync(email, password);
            var verifyToken = await GenerateVerificationTokenAsync(reg.Data!.Token);
            await VerifyEmailAsync(verifyToken);

            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = email,
                Password = "WrongPass123!"
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_CREDENTIALS");
            err.ErrorMessage.Should().Contain("incorrect");
        }

        [Fact]
        public async Task GenerateVerificationToken_WhenNoAuth_ShouldReturn401()
        {
            var res = await _client.PostAsync("/api/auth/generate-verification-token", content: null);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task GenerateVerificationToken_WhenAlreadyActive_ShouldReturn400_ALREADY_VERIFIED()
        {
            var login = await LoginOkAsync(TestSeed.AdminEmail, TestSeed.AdminPassword);
            var access = login.Data!.Token;

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/generate-verification-token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("ALREADY_VERIFIED");
            err.ErrorMessage.Should().Contain("already verified");
        }

        [Fact]
        public async Task VerifyEmail_Flow_Register_GenerateToken_Verify_Login_ShouldWork()
        {
            var email = NewEmail("flow");
            var password = "P@ssword123!";

            var reg = await RegisterStudentAsync(email, password);
            reg.Data!.EmailVerified.Should().BeFalse();
            var verifyToken = await GenerateVerificationTokenAsync(reg.Data.Token);

            await VerifyEmailAsync(verifyToken);

            var login = await LoginOkAsync(email, password);
            login.Data!.EmailVerified.Should().BeTrue();
            login.Data.Token.Should().NotBeNullOrWhiteSpace();
            login.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
        }
        public async Task VerifyEmail_WhenTokenInvalid_ShouldReturn401_INVALID_TOKEN()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailDTO
            {
                Token = "this-is-not-a-jwt"
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_TOKEN");
            err.ErrorMessage.Should().Contain("Invalid or expired");
        }

        [Fact]
        public async Task VerifyEmail_WhenUsingRefreshToken_ShouldReturn401_INVALID_TOKEN()
        {
            var email = NewEmail("verify-wrong-type");
            var password = "P@ssword123!";

            var reg = await RegisterStudentAsync(email, password);

            var res = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailDTO
            {
                Token = reg.Data!.RefreshToken
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_TOKEN");
        }

        [Fact]
        public async Task VerifyEmail_WhenEmailMismatch_ShouldReturn400_EMAIL_MISMATCH()
        {
            var emailA = NewEmail("emailA");
            var passA = "P@ssword123!";
            var regA = await RegisterStudentAsync(emailA, passA);
            var verifyTokenA = await GenerateVerificationTokenAsync(regA.Data!.Token);

            var wrongEmail = NewEmail("wrong");
            var userId = regA.Data!.UserId; 

            var badToken = JwtTestHelper.BuildToken(
                sub: userId,
                email: wrongEmail,
                issuer: "Capstone",
                audience: "Capstone:email_verify",
                key: "InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge",
                typ: "email_verify",
                expiresMinutes: 30);

            var res = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailDTO
            {
                Token = badToken
            });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_MISMATCH");
            err.ErrorMessage.Should().Contain("does not match");
        }

        [Fact]
        public async Task Refresh_WhenUserUnverified_ShouldReturn403_USER_INACTIVE()
        {
            var email = NewEmail("refreshunverified");
            var password = "P@ssword123!";

            var reg = await RegisterStudentAsync(email, password);

            var res = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDTO
            {
                RefreshToken = reg.Data!.RefreshToken
            });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("USER_INACTIVE");
            err.ErrorMessage.Should().Contain("not active");
        }

        [Fact]
        public async Task Refresh_WhenActive_ShouldReturnNewToken_AndMeShouldWork()
        {
            var login = await LoginOkAsync(TestSeed.AdminEmail, TestSeed.AdminPassword);
            var oldAccess = login.Data!.Token;
            var oldRefresh = login.Data!.RefreshToken;

            var refreshed = await RefreshOkAsync(oldRefresh);
            refreshed.Data!.Token.Should().NotBeNullOrWhiteSpace();
            refreshed.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
            refreshed.Data.Token.Should().NotBe(oldAccess);

            var meRes = await MeAsync(refreshed.Data.Token);
            meRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var meBody = await meRes.ReadOkAsync<ProfileDTO>();
            meBody.Data!.Email.Should().Be(TestSeed.AdminEmail.ToLowerInvariant());
            meBody.Data!.Role.Should().Be(RoleConstants.Admin);
        }
        [Fact]
        public async Task Refresh_WhenTokenInvalid_ShouldReturn401_INVALID_TOKEN()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDTO
            {
                RefreshToken = "not-a-jwt"
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_TOKEN");
        }

        [Fact]
        public async Task Refresh_WhenUsingAccessToken_ShouldReturn401_INVALID_TOKEN()
        {
            var login = await LoginOkAsync(TestSeed.AdminEmail, TestSeed.AdminPassword);

            var res = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDTO
            {
                RefreshToken = login.Data!.Token 
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_TOKEN");
        }


        [Fact]
        public async Task Me_WhenNoToken_ShouldReturn401()
        {
            var res = await _client.GetAsync("/api/auth/me");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ChangePassword_WrongCurrent_ShouldReturn400_BAD_CURRENT_PASSWORD()
        {
            var (email, password) = await CreateVerifiedStudentAsync();
            var login = await LoginOkAsync(email, password);
            var access = login.Data!.Token;

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            req.Content = JsonContent.Create(new ChangePasswordDTO
            {
                CurrentPassword = "WrongCurrent123!",
                NewPassword = "NewP@ssword123!",
                ConfirmNewPassword = "NewP@ssword123!"
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("BAD_CURRENT_PASSWORD");
            err.ErrorMessage.Should().Contain("incorrect");
        }

        [Fact]
        public async Task ChangePassword_Success_ShouldAllowLoginWithNewPassword()
        {
            var (email, password) = await CreateVerifiedStudentAsync();

            var login = await LoginOkAsync(email, password);
            var access = login.Data!.Token;

            var newPass = "NewP@ssword123!";

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            req.Content = JsonContent.Create(new ChangePasswordDTO
            {
                CurrentPassword = password,
                NewPassword = newPass,
                ConfirmNewPassword = newPass
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var oldLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO { Email = email, Password = password });
            oldLoginRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var newLogin = await LoginOkAsync(email, newPass);
            newLogin.Data!.Token.Should().NotBeNullOrWhiteSpace();
        }
        [Fact]
        public async Task ChangePassword_WhenNoAuth_ShouldReturn401()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordDTO
            {
                CurrentPassword = "whatever",
                NewPassword = "NewP@ssword123!",
                ConfirmNewPassword = "NewP@ssword123!"
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ForgotPassword_WhenEmailNotFound_ShouldReturn200_WithNullToken()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordDTO
            {
                Email = NewEmail("notfound")
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<ResetTokenDTO>();
            body.Data.Should().NotBeNull();
            body.Data!.Token.Should().BeNull();
        }

        [Fact]
        public async Task ResetPassword_Flow_Forgot_Reset_Login_ShouldWork()
        {
            var (email, password) = await CreateVerifiedStudentAsync();

            var forgotRes = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordDTO
            {
                Email = email
            });

            forgotRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var forgotBody = await forgotRes.ReadOkAsync<ResetTokenDTO>();
            forgotBody.Data!.Token.Should().NotBeNullOrWhiteSpace();
            var resetToken = forgotBody.Data.Token!;

            var newPass = "ResetP@ssword123!";
            var resetRes = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordDTO
            {
                Token = resetToken,
                NewPassword = newPass,
                ConfirmNewPassword = newPass
            });

            resetRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var oldLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO { Email = email, Password = password });
            oldLoginRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var newLogin = await LoginOkAsync(email, newPass);
            newLogin.Data!.Token.Should().NotBeNullOrWhiteSpace();
        }
        [Fact]
        public async Task ResetPassword_WhenTokenInvalid_ShouldReturn401_INVALID_TOKEN()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordDTO
            {
                Token = "not-a-jwt",
                NewPassword = "ResetP@ssword123!",
                ConfirmNewPassword = "ResetP@ssword123!"
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("INVALID_TOKEN");
        }

        [Fact]
        public async Task ResetPassword_WhenEmailMismatch_ShouldReturn400_EMAIL_MISMATCH()
        {
            var (email, _) = await CreateVerifiedStudentAsync();

            var forgotRes = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordDTO
            {
                Email = email
            });
            forgotRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var forgotBody = await forgotRes.ReadOkAsync<ResetTokenDTO>();
            forgotBody.Data!.Token.Should().NotBeNullOrWhiteSpace();

            var originalToken = forgotBody.Data!.Token!;
            var decoded = JwtTestHelper.ReadJwt(originalToken);
            var sub = decoded.sub!;
            sub.Should().NotBeNullOrWhiteSpace();

            var badToken = JwtTestHelper.BuildToken(
                sub: sub,
                email: NewEmail("wrong-email"),
                issuer: "Capstone",
                audience: "Capstone:password_reset",
                key: "InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge",
                typ: "password_reset",
                expiresMinutes: 30);

            var res = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordDTO
            {
                Token = badToken,
                NewPassword = "ResetP@ssword123!",
                ConfirmNewPassword = "ResetP@ssword123!"
            });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("EMAIL_MISMATCH");
        }

        [Fact]
        public async Task RegisterJudge_WhenAdmin_ShouldReturn200_AndRoleJudge()
        {
            var login = await LoginOkAsync(TestSeed.AdminEmail, TestSeed.AdminPassword);
            var adminAccess = login.Data!.Token;

            var dto = new RegisterUserDTO
            {
                FullName = "Judge A",
                Email = NewEmail("judge"),
                Password = "P@ssword123!",
                ConfirmPassword = "P@ssword123!"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register-judge");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccess);
            req.Content = JsonContent.Create(dto);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await res.ReadOkAsync<ProfileDTO>();
            body.Data!.Role.Should().Be(RoleConstants.Judge);
            body.Data!.Email.Should().Be(dto.Email.Trim().ToLowerInvariant());
            body.Data!.Status.Should().Be(UserStatusConstants.Active);
        }

        [Fact]
        public async Task RegisterJudge_WhenNotAdmin_ShouldReturn403()
        {
            var (email, password) = await CreateVerifiedStudentAsync();
            var login = await LoginOkAsync(email, password);
            var access = login.Data!.Token;

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register-judge");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            req.Content = JsonContent.Create(new RegisterUserDTO
            {
                FullName = "Judge X",
                Email = NewEmail("judgeX"),
                Password = "P@ssword123!"
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        [Fact]
        public async Task RegisterJudge_WhenMissingConfirmPassword_ShouldReturn400()
        {
            var login = await LoginOkAsync(TestSeed.AdminEmail, TestSeed.AdminPassword);
            var adminAccess = login.Data!.Token;

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register-judge");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccess);
            req.Content = JsonContent.Create(new RegisterUserDTO
            {
                FullName = "Judge Missing Confirm",
                Email = NewEmail("judge-missing-confirm"),
                Password = "P@ssword123!",
                ConfirmPassword = null // intentionally
            });

            var res = await _client.SendAsync(req);

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().NotBeNullOrWhiteSpace();
        }

    }
}
