using BusinessLogic.IServices.Schools;
using DataAccess.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.SchoolDTOs;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Schools
{
    [Route("api/school-creation-requests")]
    [ApiController]
    public class SchoolCreationRequestsController : ControllerBase
    {
        private readonly ISchoolCreationRequestService _service;

        public SchoolCreationRequestsController(ISchoolCreationRequestService service) => _service = service;

        // School manager creates request + evidences
        [HttpPost]
        [Authorize(Roles = RoleConstants.SchoolManager)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromForm] CreateSchoolCreationRequestFormDTO dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var created = await _service.CreateAsync(dto, userId);

            return StatusCode(StatusCodes.Status201Created,
                new BaseResponseModel<object>(StatusCodes.Status201Created, ResponseCodeConstants.SUCCESS, created, message: "School creation request submitted."));
        }

        // Staff/Admin list
        [HttpGet]
        [Authorize(Roles = $"{RoleConstants.Admin},{RoleConstants.Staff}")]
        public async Task<IActionResult> List([FromQuery] SchoolCreationRequestQueryParams query)
        {
            var page = await _service.GetListAsync(query);
            var meta = new
            {
                page.PageNumber,
                page.PageSize,
                page.TotalPages,
                page.TotalCount,
                page.HasPreviousPage,
                page.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, meta, "OK"));
        }

        // School manager: my requests
        [HttpGet("my")]
        [Authorize(Roles = RoleConstants.SchoolManager)]
        public async Task<IActionResult> My([FromQuery] SchoolCreationRequestQueryParams query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var page = await _service.GetMyAsync(userId, query);

            var meta = new
            {
                page.PageNumber,
                page.PageSize,
                page.TotalPages,
                page.TotalCount,
                page.HasPreviousPage,
                page.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, meta, "OK"));
        }

        // Detail: staff/admin OR requester
        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> Get(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            var item = await _service.GetByIdAsync(id, userId, role);
            return Ok(BaseResponseModel<object>.OkResponseModel(item, "OK"));
        }

        [HttpPost("{id:guid}/approve")]
        [Authorize(Roles = $"{RoleConstants.Admin},{RoleConstants.Staff}")]
        public async Task<IActionResult> Approve(Guid id)
        {
            var reviewerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.ApproveAsync(id, reviewerId);
            return Ok(BaseResponseModel<object>.OkResponseModel(null, "Approved."));
        }

        [HttpPost("{id:guid}/deny")]
        [Authorize(Roles = $"{RoleConstants.Admin},{RoleConstants.Staff}")]
        public async Task<IActionResult> Deny(Guid id, [FromBody] DenySchoolCreationRequestDTO dto)
        {
            var reviewerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.DenyAsync(id, reviewerId, dto.DenyReason);
            return Ok(BaseResponseModel<object>.OkResponseModel(null, "Denied."));
        }
    }
}

