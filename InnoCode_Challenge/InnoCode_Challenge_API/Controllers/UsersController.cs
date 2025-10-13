using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.UserDTOs;
using Repository.DTOs.UserDTOs.Repository.DTOs.UserDTOs;
using Repository.ResponseModel;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Utility.Constant;

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
        //[Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> GetAll([FromQuery] UserQueryParams query)
        {
            var page = await _userService.GetUsersAsync(query);

            var additional = new
            {
                page.PageNumber,
                page.PageSize,
                page.TotalPages,
                page.TotalCount,
                page.HasPreviousPage,
                page.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: page.Items,
                additionalData: additional,
                message: "Users retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        [Authorize] 
        public async Task<IActionResult> GetById(Guid id)
        {
            var loggedInRole = User.FindFirstValue(ClaimTypes.Role);
            var loggedInId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (loggedInRole == RoleConstants.Admin || loggedInRole == RoleConstants.Staff || loggedInId == id.ToString())
            {
                var dto = await _userService.GetUserByIdAsync(id);
                return Ok(new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status200OK,
                    code: ResponseCodeConstants.SUCCESS,
                    data: dto,
                    message: "Users retrieved successfully."
                ));
            }

            return StatusCode(StatusCodes.Status403Forbidden,
                new BaseResponseModel(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN, "Forbidden"));
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

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDTO dto)
        {
            var performedByRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var updated = await _userService.UpdateUserAsync(id, dto, performedByRole);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updated,
                message: "User updated successfully."
            ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            var deletedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await _userService.DeleteUserAsync(id, deletedBy);
            return NoContent();
        }
    }
}
