using AutoMapper;
using BusinessLogic.IServices.NotificationsAndLogs;
using BusinessLogic.IServices.Users;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Repository.DTOs.AuthDTOs;
using Repository.IRepositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.ConfigDTOs;

namespace BusinessLogic.Services.Users
{
    public class AuthService : IAuthService
    {
        private readonly IUOW _unitOfWork;
        private readonly JwtSettings _jwtSettings;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IActivityLogWriter _logWriter;

        public AuthService(
          IUOW unitOfWork,
          IOptions<JwtSettings> jwtConfig,
          IHttpContextAccessor httpContextAccessor,
          IActivityLogWriter logWriter)
        {
            _unitOfWork = unitOfWork;
            _jwtSettings = jwtConfig.Value;
            _httpContextAccessor = httpContextAccessor;
            _logWriter = logWriter;
        }
        public async Task<AuthResponseDTO> RegisterStudentStrictAsync(RegisterStudentDTO dto)
        {
            //Check password and confirm password match
            if (!string.Equals(dto.Password, dto.ConfirmPassword, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "CONFIRM_PASSWORD_MISMATCH", "Passwords do not match.");

            // Check SchoolId not null or empty
            if (dto.SchoolId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    "SCHOOL_ID_REQUIRED", "SchoolId is required.");

            var email = NormalizeEmail(dto.Email);
            await EnsureEmailNotExistsAsync(email);

            var userRepo = _unitOfWork.GetRepository<User>();
            var studentRepo = _unitOfWork.GetRepository<Student>();
            var schoolRepo = _unitOfWork.GetRepository<School>();


            // Check SchoolId present and valid
            var school = await schoolRepo.Entities
                .FirstOrDefaultAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
            if (school == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId}");

            var now = DateTime.UtcNow;

            var user = CreateUser(
                email: email,
                fullName: dto.FullName,
                password: dto.Password,
                role: RoleConstants.Student,
                status: UserStatusConstants.Unverified,
                nowUtc: now
            );


            var student = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = user.UserId,
                SchoolId = dto.SchoolId,
                Grade = string.IsNullOrWhiteSpace(dto.Grade) ? null : dto.Grade.Trim(),
                CreatedAt = now
            };

            _unitOfWork.BeginTransaction();
            try
            {
                await userRepo.InsertAsync(user);
                await _unitOfWork.SaveAsync();

                await studentRepo.InsertAsync(student);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }

            await _logWriter.TryWriteAsync(
                user.UserId,
                ActivityActions.UserRegister,
                TargetTypes.User,
                user.UserId.ToString());

            var accessToken = GenerateJwtToken(user);
            var (refreshToken, refreshExp) = GenerateRefreshToken(user);

            return new AuthResponseDTO
            {
                Token = accessToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                RefreshToken = refreshToken,
                RefreshExpiresAt = refreshExp,
                UserId = user.UserId.ToString(),
                Role = user.Role,
                FullName = user.Fullname,
                Email = user.Email,
                EmailVerified = false // Unverified right after signup
            };
        }

        public async Task<AuthResponseDTO> RegisterAsync(RegisterUserDTO dto)
        {
            var email = NormalizeEmail(dto.Email);
            await EnsureEmailNotExistsAsync(email);

            var now = DateTime.UtcNow;

            var user = CreateUser(
                email: email,
                fullName: dto.FullName,
                password: dto.Password,
                role: RoleConstants.Student,
                status: UserStatusConstants.Unverified,
                nowUtc: now
            );

            var userRepo = _unitOfWork.GetRepository<User>();
            await userRepo.InsertAsync(user);
            await _unitOfWork.SaveAsync();

            await _logWriter.TryWriteAsync(
                user.UserId,
                ActivityActions.UserRegister,
                TargetTypes.User,
                user.UserId.ToString());

            var accessToken = GenerateJwtToken(user);
            var (refreshToken, refreshExp) = GenerateRefreshToken(user); 
            
            return new AuthResponseDTO
            {
                Token = accessToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                RefreshToken = refreshToken,             
                RefreshExpiresAt = refreshExp,           
                UserId = user.UserId.ToString(),
                Role = user.Role,
                FullName = user.Fullname,
                Email = user.Email,
                EmailVerified = string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase)
            };
        }

        public async Task<AuthResponseDTO> LoginAsync(LoginDTO dto)
        {
            var email = NormalizeEmail(dto.Email);
            var userRepo = _unitOfWork.GetRepository<User>();

            var user = await userRepo.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null);

            if (user == null || !PasswordHasher.Verify(dto.Password, user.PasswordHash))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Email or password is incorrect.");

            if (string.Equals(user.Status, UserStatusConstants.Unverified, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "USER_UNVERIFIED", "Please verify your email.");


            if (!string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(
                  StatusCodes.Status403Forbidden,
                  "USER_INACTIVE",
                  "Your account has been disabled."
                );

            var accessToken = GenerateJwtToken(user);
            var (refreshToken, refreshExp) = GenerateRefreshToken(user);

            await _logWriter.TryWriteAsync(
                user.UserId,
                ActivityActions.UserLogin,
                TargetTypes.User,
                user.UserId.ToString());

            return new AuthResponseDTO
            {
                Token = accessToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                RefreshToken = refreshToken,               
                RefreshExpiresAt = refreshExp,             
                UserId = user.UserId.ToString(),
                Role = user.Role,
                FullName = user.Fullname,
                Email = user.Email,
                EmailVerified = string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase) 

            };
        }
        public async Task<string> GenerateVerificationTokenAsync()
        {
            var user = await GetCurrentLoggedInUser()
                ?? throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            if (string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status400BadRequest, "ALREADY_VERIFIED", "Email already verified.");

            return GenerateVerificationToken(user);
        }

        public async Task VerifyEmailAsync(string token)
        {
            var principal = ValidateToken(
                token,
                audience: _jwtSettings.Audience + ":email_verify",
                requireType: "email_verify"
            );

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
          ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            var email = GetClaim(principal, JwtRegisteredClaimNames.Email, ClaimTypes.Email, "email") ?? string.Empty;

            if (!Guid.TryParse(sub, out var userId))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_TOKEN", "Invalid user.");

            var user = await GetUserIfNotDeletedAsync(userId)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status400BadRequest, "EMAIL_MISMATCH", "Token does not match user email.");

            if (!string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
            {
                user.Status = UserStatusConstants.Active;
                user.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveAsync();
            }
        }

        public async Task ChangePasswordAsync(ChangePasswordDTO dto)
        {
            var user = await GetCurrentLoggedInUser()
                ?? throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            if (!PasswordHasher.Verify(dto.CurrentPassword, user.PasswordHash))
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_CURRENT_PASSWORD", "Current password is incorrect.");

            user.PasswordHash = PasswordHasher.Hash(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.SaveAsync();

            await _logWriter.TryWriteAsync(
                user.UserId,
                ActivityActions.UserPasswordChange,
                TargetTypes.User,
                user.UserId.ToString());
        }

        public async Task<AuthResponseDTO> RefreshAsync(string refreshToken)
        {
            var principal = ValidateToken(
                refreshToken,
                audience: _jwtSettings.Audience + ":refresh",
                requireType: "refresh"
            );

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
          ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(sub, out var userId))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_REFRESH", "Invalid user.");

            var user = await GetUserIfNotDeletedAsync(userId)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (!string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "USER_INACTIVE", "Your account is not active.");

            var access = GenerateJwtToken(user);
            var (newRefresh, refreshExp) = GenerateRefreshToken(user);

            return new AuthResponseDTO
            {
                Token = access,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                RefreshToken = newRefresh,
                RefreshExpiresAt = refreshExp,
                UserId = user.UserId.ToString(),
                Role = user.Role,
                FullName = user.Fullname,
                Email = user.Email,
                EmailVerified = true
            };
        }
        public Task LogoutAsync(string refreshToken)
        {
            return Task.CompletedTask;
        }


        public Task<ProfileDTO> RegisterJudgeAsync(RegisterUserDTO dto)
        {
            return RegisterSystemUserAsync(dto, RoleConstants.Judge);
        }

        public Task<ProfileDTO> RegisterAdminAsync(RegisterUserDTO dto)
        {
            return RegisterSystemUserAsync(dto, RoleConstants.Admin);
        }

        public Task<ProfileDTO> RegisterStaffAsync(RegisterUserDTO dto)
        {
            return RegisterSystemUserAsync(dto, RoleConstants.Staff);
        }

        public Task<ProfileDTO> RegisterOrganizerAsync(RegisterUserDTO dto)
        {
            return RegisterSystemUserAsync(dto, RoleConstants.ContestOrganizer);
        }

        private async Task<ProfileDTO> RegisterSystemUserAsync(RegisterUserDTO dto, string role)
        {
            var email = NormalizeEmail(dto.Email);
            await EnsureEmailNotExistsAsync(email);

            var now = DateTime.UtcNow;

            var user = CreateUser(
                email: email,
                fullName: dto.FullName,
                password: dto.Password,
                role: role,
                status: UserStatusConstants.Active,
                nowUtc: now
            );

            var userRepo = _unitOfWork.GetRepository<User>();
            await userRepo.InsertAsync(user);
            await _unitOfWork.SaveAsync();

            await _logWriter.TryWriteAsync(
                user.UserId,
                ActivityActions.UserRegister,
                TargetTypes.User,
                user.UserId.ToString());

            return new ProfileDTO
            {
                UserId = user.UserId.ToString(),
                Email = user.Email,
                FullName = user.Fullname,
                Role = user.Role,
                Status = user.Status,
                CreatedAt = user.CreatedAt
            };
        }


        private string GenerateJwtToken(User user)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(ClaimTypes.Role, user.Role),
                new(ClaimTypes.Name, user.Fullname ?? string.Empty),
                new("email_verified", string.Equals(user.Status, UserStatusConstants.Active, StringComparison.OrdinalIgnoreCase)
                    .ToString().ToLowerInvariant()),
                new("typ", "access"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64),
            };

            return JwtTokenHelper.GenerateToken(
                settings: _jwtSettings,
                audience: _jwtSettings.Audience,
                expiresUtc: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                claims: claims
            );

        }

        private (string token, DateTime expiresAt) GenerateRefreshToken(User user)
        {
            var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.RefreshExpiryMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new("typ", "refresh"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = JwtTokenHelper.GenerateToken(
                settings: _jwtSettings,
                audience: _jwtSettings.Audience + ":refresh",
                expiresUtc: expires,
                claims: claims
            );

            return (token, expires);
        }

        public async Task<string?> GenerateResetPasswordTokenAsync(string emailInput)
        {
            var email = NormalizeEmail(emailInput);

            var repo = _unitOfWork.GetRepository<User>();
            var user = await repo.Entities
                .FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null);

            if (user is null) return null;

            return GeneratePasswordResetToken(user);
        }

        public async Task ResetPasswordAsync(ResetPasswordDTO dto)
        {
            var principal = ValidateToken(
                dto.Token,
                audience: _jwtSettings.Audience + ":password_reset",
                requireType: "password_reset"
            );

            var sub = GetClaim(principal, JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier);
            var tokenEmail = GetClaim(principal, JwtRegisteredClaimNames.Email, ClaimTypes.Email, "email") ?? string.Empty;

            if (!Guid.TryParse(sub, out var userId))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_TOKEN", "Invalid user.");

            var user = await GetUserIfNotDeletedAsync(userId)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "User not found.");

            if (!string.Equals(user.Email, tokenEmail, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status400BadRequest, "EMAIL_MISMATCH", "Token does not match user email.");

            user.PasswordHash = PasswordHasher.Hash(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveAsync();
        }

        private string GeneratePasswordResetToken(User user)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new("typ", "password_reset"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            return JwtTokenHelper.GenerateToken(
                settings: _jwtSettings,
                audience: _jwtSettings.Audience + ":password_reset",
                expiresUtc: DateTime.UtcNow.AddMinutes(30),
                claims: claims
            );


        }

        private string GenerateVerificationToken(User user)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new("typ", "email_verify"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            return JwtTokenHelper.GenerateToken(
                settings: _jwtSettings,
                audience: _jwtSettings.Audience + ":email_verify",
                expiresUtc: DateTime.UtcNow.AddMinutes(30),
                claims: claims
            );
        }

        private ClaimsPrincipal ValidateToken(string token, string audience, string requireType)
        {
            var handler = new JwtSecurityTokenHandler();

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            ClaimsPrincipal principal;
            try
            {
                principal = handler.ValidateToken(token, parameters, out _);
            }
            catch
            {
                throw new ErrorException(StatusCodes.Status401Unauthorized, "INVALID_TOKEN", "Invalid or expired token.");
            }

            var typ = principal.FindFirst("typ")?.Value;
            if (!string.Equals(typ, requireType, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_TOKEN_TYPE", "Wrong token type.");

            return principal;
        }

        public async Task<User?> GetCurrentLoggedInUser()
        {
            var currentId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrWhiteSpace(currentId) || !Guid.TryParse(currentId, out var id))
                return null;

            return await GetUserIfNotDeletedAsync(id);
        }

        private static string NormalizeEmail(string email) => EmailHelper.Normalize(email);
        private static string? GetClaim(ClaimsPrincipal p, params string[] types)
        {
            foreach (var t in types)
            {
                var v = p.FindFirst(t)?.Value;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }
        private async Task EnsureEmailNotExistsAsync(string email)
        {
            var userRepo = _unitOfWork.GetRepository<User>();
            bool emailExists = await userRepo.Entities
                .AnyAsync(u => u.Email.ToLower() == email && u.DeletedAt == null);

            if (emailExists)
                throw new ErrorException(StatusCodes.Status400BadRequest, "EMAIL_EXISTS", "Email is already registered.");
        }

        private static User CreateUser(
            string email,
            string fullName,
            string password,
            string role,
            string status,
            DateTime nowUtc)
        {
            return new User
            {
                UserId = Guid.NewGuid(),
                Fullname = fullName.Trim(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = role,
                Status = status,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                DeletedAt = null
            };
        }

        private async Task<User?> GetUserIfNotDeletedAsync(Guid userId)
        {
            var repo = _unitOfWork.GetRepository<User>();
            var user = await repo.GetByIdAsync(userId);

            if (user is null) return null;
            if (user.DeletedAt != null) return null;

            return user;
        }

    }
}
