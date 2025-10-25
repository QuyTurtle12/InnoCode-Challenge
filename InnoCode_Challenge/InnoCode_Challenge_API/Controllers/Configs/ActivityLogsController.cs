using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ActivityLogDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ActivityLogsController : ControllerBase
    {
        private readonly IActivityLogService _service;

        public ActivityLogsController(IActivityLogService service) => _service = service;

        [HttpGet]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Get([FromQuery] ActivityLogQueryParams query)
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
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, additional, "Logs retrieved."
            ));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var data = await _service.GetByIdAsync(id);
            return Ok(BaseResponseModel<object>.OkResponseModel(data, "Log fetched."));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateActivityLogDTO dto)
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.LogId },
                new BaseResponseModel<object>(StatusCodes.Status201Created, ResponseCodeConstants.SUCCESS, created, message: "Log created.")
            );
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }
    }
}
