using BusinessLogic.IServices.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.AuthDTOs.Repository.DTOs.AuthDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Users
{

  [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterStudentDTO dto)
        {
            var result = await _authService.RegisterStudentStrictAsync(dto);

            var response = new BaseResponseModel<AuthResponseDTO>(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Student registered successfully."
            );

            return StatusCode(StatusCodes.Status201Created, response);
        }


        [HttpPost("register-judge")]
        public async Task<IActionResult> RegisterJudge([FromBody] RegisterUserDTO dto)
        {
            var result = await _authService.RegisterJudgeAsync(dto);

            var response = new BaseResponseModel<ProfileDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Judge registered successfully."
            );

            return Ok(response);
        }

        [HttpPost("register-admin")]
        public async Task<IActionResult> RegisterAdmin([FromBody] RegisterUserDTO dto)
        {
            var result = await _authService.RegisterAdminAsync(dto);

            var response = new BaseResponseModel<ProfileDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Admin registered successfully."
            );

            return Ok(response);
        }

        [HttpPost("register-staff")]
        public async Task<IActionResult> RegisterStaff([FromBody] RegisterUserDTO dto)
        {
            var result = await _authService.RegisterStaffAsync(dto);

            var response = new BaseResponseModel<ProfileDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Staff registered successfully."
            );

            return Ok(response);
        }

        [HttpPost("register-organizer")]
        public async Task<IActionResult> RegisterOrganizer([FromBody] RegisterUserDTO dto)
        {
            var result = await _authService.RegisterOrganizerAsync(dto);

            var response = new BaseResponseModel<ProfileDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Organizer registered successfully."
            );

            return Ok(response);
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDTO dto)
        {
            var result = await _authService.LoginAsync(dto);

            var response = new BaseResponseModel<AuthResponseDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Login successful."
            );

            return Ok(response);
        }

        [Authorize]
        [HttpPost("generate-verification-token")]
        public async Task<IActionResult> GenerateVerificationToken()
        {
            var token = await _authService.GenerateVerificationTokenAsync();

            var response = new BaseResponseModel<VerificationTokenDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: new VerificationTokenDTO { Token = token },
                message: "Verification token generated."
            );

            return Ok(response);
        }


        [AllowAnonymous]
        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDTO dto)
        {
            await _authService.VerifyEmailAsync(dto.Token);

            var response = new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: null,
                message: "Email verified."
            );

            return Ok(response);
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            await _authService.ChangePasswordAsync(dto);

            var response = new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: null,
                message: "Password changed."
            );

            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequestDTO dto)
        {
            var result = await _authService.RefreshAsync(dto.RefreshToken);

            var response = new BaseResponseModel<AuthResponseDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Token refreshed."
            );

            return Ok(response);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequestDTO dto)
        {
            await _authService.LogoutAsync(dto.RefreshToken);

            var response = new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: null,
                message: "Logged out."
            );

            return Ok(response);
        }
        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
        {
            var token = await _authService.GenerateResetPasswordTokenAsync(dto.Email);

            var response = new BaseResponseModel<ResetTokenDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: new ResetTokenDTO { Token = token }, // may be null if email not found
                message: "If the account exists, a reset token has been generated."
            );

            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
        {
            await _authService.ResetPasswordAsync(dto);

            var response = new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: null,
                message: "Password reset successfully."
            );

            return Ok(response);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var user = await _authService.GetCurrentLoggedInUser();
            if (user is null) return Unauthorized();

            var profile = new ProfileDTO
            {
                UserId = user.UserId.ToString(),
                Email = user.Email,
                FullName = user.Fullname,
                Role = user.Role,
                Status = user.Status,
                CreatedAt = user.CreatedAt
            };

            var response = new BaseResponseModel<ProfileDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: profile,
                message: "OK"
            );

            return Ok(response);
        }

    }
}
