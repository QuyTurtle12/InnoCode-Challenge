using BusinessLogic.IServices.Users;
using DataAccess.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.RoleRegistrationDTOs;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Users
{
    [Route("api/role-registrations")]
    [ApiController]
    public class RoleRegistrationsController : ControllerBase
    {
        private readonly IRoleRegistrationService _service;

        public RoleRegistrationsController(IRoleRegistrationService service) => _service = service;

        [HttpPost]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Submit([FromForm] CreateRoleRegistrationDTO dto)
        {
            var created = await _service.SubmitAsync(dto);
            return StatusCode(StatusCodes.Status201Created,
                new BaseResponseModel<RoleRegistrationSubmittedDTO>(
                    StatusCodes.Status201Created,
                    ResponseCodeConstants.SUCCESS,
                    created,
                    "Role registration submitted."
                ));
        }

        [HttpGet]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> List([FromQuery] RoleRegistrationQueryParams query)
        {
            PaginatedList<RoleRegistrationDTO> page = await _service.GetAsync(query);

            var meta = new
            {
                page.PageNumber,
                page.PageSize,
                page.TotalPages,
                page.TotalCount,
                page.HasPreviousPage,
                page.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                page.Items,
                meta,
                "Role registrations retrieved."
            ));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var data = await _service.GetByIdAsync(id);
            return Ok(BaseResponseModel<object>.OkResponseModel(data, "Role registration fetched."));
        }

        [HttpPost("{id:guid}/approve")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Approve(Guid id)
        {
            var reviewerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.ApproveAsync(id, reviewerId);

            return Ok(new BaseResponseModel(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                "Role registration approved and user created."
            ));
        }

        [HttpPost("{id:guid}/deny")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Deny(Guid id, [FromBody] DenyRoleRegistrationDTO dto)
        {
            var reviewerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.DenyAsync(id, dto.Reason, reviewerId);

            return Ok(new BaseResponseModel(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                "Role registration denied."
            ));
        }
    }

}
