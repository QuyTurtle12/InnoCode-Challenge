using BusinessLogic.IServices.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.AuthDTOs;
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
    }
}
