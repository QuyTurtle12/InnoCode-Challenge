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
   // [Authorize] // any authenticated user (User/Expert/Admin)
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// GET /api/users
        /// Only Admin can list all users.
        /// </summary>
        [HttpGet]
        // [Authorize(Roles = RoleConstants.Admin)]
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

        /// <summary>
        /// GET /api/users/{id}
        /// Admin can retrieve any user.
        /// A User or Expert can retrieve only their own profile.
        /// </summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var loggedInRole = User.FindFirstValue(ClaimTypes.Role);
            var loggedInId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Admin → any user
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

            // Otherwise (User or Expert) → only their own
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

        /// <summary>
        /// POST /api/users
        /// Only Admin can create new users.
        /// </summary>
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

        /// <summary>
        /// PUT /api/users/{id}
        /// Admin can update any user.
        /// A User or Expert can update only their own profile (and cannot change their own Role).
        /// </summary>
        //[HttpPut("{id:guid}")]
        //public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDTO dto)
        //{
        //    if (id != dto.Id)
        //    {
        //        return BadRequest(new BaseResponseModel<object>(
        //            statusCode: StatusCodes.Status400BadRequest,
        //            code: ResponseCodeConstants.BADREQUEST,
        //            data: null!,
        //            message: "ID in path and body do not match."
        //        ));
        //    }

        //    var loggedInRole = User.FindFirstValue(ClaimTypes.Role);
        //    var loggedInId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        //    // Admin → update anyone
        //    if (loggedInRole == RoleConstants.Admin)
        //    {
        //        var updated = await _userService.UpdateUserAsync(dto);
        //        return Ok(new BaseResponseModel<object>(
        //            statusCode: StatusCodes.Status200OK,
        //            code: ResponseCodeConstants.SUCCESS,
        //            data: updated,
        //            message: "User updated by Admin."
        //        ));
        //    }

        //    // User or Expert → can only update self, cannot change role
        //    if ((loggedInRole == RoleConstants.User || loggedInRole == RoleConstants.Expert)
        //        && loggedInId == id.ToString())
        //    {
        //        // Prevent role escalation
        //        if (!string.IsNullOrWhiteSpace(dto.Role)
        //            && RoleConstants.ToRoleValue(dto.Role) != RoleConstants.UserValue
        //            && RoleConstants.ToRoleValue(dto.Role) != RoleConstants.ExpertValue)
        //        {
        //            return Forbid();
        //        }

        //        var updated = await _userService.UpdateUserAsync(dto);
        //        return Ok(new BaseResponseModel<object>(
        //            statusCode: StatusCodes.Status200OK,
        //            code: ResponseCodeConstants.SUCCESS,
        //            data: updated,
        //            message: "Your profile updated successfully."
        //        ));
        //    }

        //    return Forbid();
        //}

        /// <summary>
        /// DELETE /api/users/{id}
        /// Only Admin can delete users.
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            // Capture the admin’s user‐id from JWT:
            var deletedBy = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await _userService.DeleteUserAsync(id, deletedBy);
            return NoContent();
        }
    }
}
