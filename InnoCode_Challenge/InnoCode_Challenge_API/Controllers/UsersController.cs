using BusinessLogic.IServices;
using Utility.Constant;
using Repository.DTOs.UserDTOs;
using Repository.ResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: users,
                message: "Users retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var loggedInRole = User.FindFirstValue(ClaimTypes.Role);
            var loggedInId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (loggedInRole == RoleConstants.Admin)
            {
                var dto = await _userService.GetUserByIdAsync(id);
                return Ok(new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status200OK,
                    code: ResponseCodeConstants.SUCCESS,
                    data: dto,
                    message: "User retrieved successfully."
                ));
            }

            if (loggedInId == id.ToString())
            {
                var dto = await _userService.GetUserByIdAsync(id);
                return Ok(new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status200OK,
                    code: ResponseCodeConstants.SUCCESS,
                    data: dto,
                    message: "Your profile retrieved successfully."
                ));
            }

            return Forbid();
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateUserDTO dto)
        {
            var created = await _userService.CreateUserAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.UserId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "User created successfully."
                ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            var deletedBy = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await _userService.DeleteUserAsync(id, deletedBy);
            return NoContent();
        }
    }
}
