using AutoMapper;
using BusinessLogic.IServices;
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

namespace BusinessLogic.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;
        private readonly JwtSettings _jwtSettings;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthService(
          IUOW unitOfWork,
          IMapper mapper,
          IOptions<JwtSettings> jwtConfig,
          IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _jwtSettings = jwtConfig.Value;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<AuthResponseDTO> RegisterAsync(RegisterUserDTO dto)
        {
            var email = NormalizeEmail(dto.Email);

            var userRepo = _unitOfWork.GetRepository<User>();
            bool emailExists = userRepo.Entities.Any(u => u.Email == dto.Email);

            if (emailExists)
                throw new ErrorException(
                  StatusCodes.Status400BadRequest,
                  "EMAIL_EXISTS",
                  "Email is already registered."
                );

            var now = DateTime.UtcNow;

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Fullname = dto.FullName.Trim(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(dto.Password),
                Role = RoleConstants.Student,
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await userRepo.InsertAsync(user);
            await _unitOfWork.SaveAsync();

            var token = GenerateJwtToken(user);

            return new AuthResponseDTO
            {
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                UserId = user.UserId.ToString(),
                Role = user.Role,
                FullName = user.Fullname,
                Email = user.Email
            };
        }

        public async Task<AuthResponseDTO> LoginAsync(LoginDTO dto)
        {
            var email = NormalizeEmail(dto.Email);
            var userRepo = _unitOfWork.GetRepository<User>();

            var user = await userRepo.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.DeletedAt == null);

            if (user == null || !PasswordHasher.Verify(dto.Password, user.PasswordHash))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Email or password is incorrect.");


            if (!string.Equals(user.Status, "Active", StringComparison.Ordinal))
                throw new ErrorException(
                  StatusCodes.Status403Forbidden,
                  "USER_INACTIVE",
                  "Your account has been disabled."
                );

            var token = GenerateJwtToken(user);

            return new AuthResponseDTO
            {
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                UserId = user.UserId.ToString(),
                Role = user.Role,
                FullName = user.Fullname,
                Email = user.Email
            };
        }

        private string GenerateJwtToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(ClaimTypes.NameIdentifier,    user.UserId.ToString()), 
                new Claim(JwtRegisteredClaimNames.Email,user.Email),
                new Claim(ClaimTypes.Role,              user.Role),
                new Claim(ClaimTypes.Name,              user.Fullname ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,  EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64),
            };

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<User?> GetCurrentLoggedInUser()
        {
            var currentId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrWhiteSpace(currentId) || !Guid.TryParse(currentId, out var id))
                return null;

            return await _unitOfWork.GetRepository<User>().GetByIdAsync(id);
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    }
}
