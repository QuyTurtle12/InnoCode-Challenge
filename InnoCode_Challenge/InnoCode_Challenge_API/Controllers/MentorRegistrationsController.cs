using System.Security.Claims;
using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.MentorRegistrationDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MentorRegistrationsController : ControllerBase
    {
        private readonly IMentorRegistrationService _service;

        public MentorRegistrationsController(IMentorRegistrationService service)
        {
            _service = service;
        }

        // Public submit
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Submit([FromBody] RegisterMentorDTO dto)
        {
            var result = await _service.SubmitAsync(dto);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, result,
                message: "Registration received."
            ));
        }

        // Staff/Admin list
        [HttpGet]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Get([FromQuery] MentorRegistrationQueryParams query)
        {
            var page = await _service.GetAsync(query);
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
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, additional,
                message: "Registrations retrieved."
            ));
        }

        // Staff/Admin get by id
        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var item = await _service.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, item,
                message: "Registration detail."
            ));
        }

        // Staff/Admin approve
        [HttpPost("{id:guid}/approve")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveMentorRegistrationDTO dto)
        {
            var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(staffId) || !Guid.TryParse(staffId, out var reviewerId))
                return Unauthorized(new BaseResponseModel(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "Invalid staff token."));

            var result = await _service.ApproveAsync(id, dto, reviewerId);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, result,
                message: "Registration approved. Mentor account created."
            ));
        }

        // Staff/Admin deny
        [HttpPost("{id:guid}/deny")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Deny(Guid id, [FromBody] DenyMentorRegistrationDTO dto)
        {
            var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(staffId) || !Guid.TryParse(staffId, out var reviewerId))
                return Unauthorized(new BaseResponseModel(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "Invalid staff token."));

            var result = await _service.DenyAsync(id, dto, reviewerId);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, result,
                message: "Registration denied."
            ));
        }
    }
}
