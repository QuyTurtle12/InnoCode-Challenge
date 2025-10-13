using BusinessLogic.IServices;
using Utility.Constant;
using Repository.DTOs.AuthDTOs;
using Repository.ResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InnoCode_Challenge_API.Controllers
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
        public async Task<IActionResult> Register([FromBody] RegisterUserDTO dto)
        {
            var result = await _authService.RegisterAsync(dto);

            var response = new BaseResponseModel<AuthResponseDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "User registered successfully."
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

        [HttpGet("test/AdminOrExpert")]
        [Authorize(Policy = "RequireExpertOrAdmin")]
        public async Task<IActionResult> RequireExpertOrAdmin()
        {
            return Ok();
        }

        [HttpGet("test/admin")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> RequireAdminRole()
        {
            return Ok();
        }

        [HttpGet("test/user")]
        [Authorize(Policy = "RequireAnyUserRole")]
        public async Task<IActionResult> RequireAnyUserRole()
        {
            return Ok();
        }


    }
}
