using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ConfigDTOs;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfigsController : ControllerBase
    {
        private readonly IConfigService _service;
        public ConfigsController(IConfigService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] ConfigQueryParams query)
        {
            var page = await _service.GetAsync(query);
            var meta = new { page.PageNumber, page.PageSize, page.TotalPages, page.TotalCount, page.HasPreviousPage, page.HasNextPage };
            return Ok(new BaseResponseModel<object>(StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, meta, "Configs retrieved."));
        }

        [HttpGet("{key}")]
        public async Task<IActionResult> GetByKey(string key)
        {
            var item = await _service.GetByKeyAsync(key);
            return Ok(BaseResponseModel<object>.OkResponseModel(item,"Config retrieved."));
        }

        [HttpPost]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Create([FromBody] CreateConfigDTO dto)
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var created = await _service.CreateAsync(dto, role);
            return CreatedAtAction(nameof(GetByKey), new { key = created.Key },
                new BaseResponseModel<object>(StatusCodes.Status201Created, ResponseCodeConstants.SUCCESS, created, message: "Config created."));
        }

        [HttpPut("{key}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Update(string key, [FromBody] UpdateConfigDTO dto)
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var updated = await _service.UpdateAsync(key, dto, role);
            return Ok(BaseResponseModel<object>.OkResponseModel(updated, "Config updated."));
        }

        [HttpDelete("{key}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Delete(string key)
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            await _service.DeleteAsync(key, role);
            return NoContent();
        }


        [HttpPut("contests/{contestId:guid}/registration-window")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> SetRegistrationWindow(Guid contestId, [FromBody] SetRegistrationWindowDTO dto)
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            await _service.SetRegistrationWindowAsync(contestId, dto, role);
            return Ok(BaseResponseModel<object>.OkResponseModel(new { contestId, dto.RegistrationStartUtc, dto.RegistrationEndUtc }, "Registration window set."));
        }

        [HttpPut("contests/{contestId:guid}/policy")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> SetContestPolicy(Guid contestId, [FromBody] SetContestPolicyDTO dto)
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            await _service.SetContestPolicyAsync(contestId, dto, role);
            return Ok(BaseResponseModel<object>.OkResponseModel(new { contestId, dto.TeamMembersMax, dto.TeamInviteTtlDays }, "Contest policy updated."));
        }
    }
}
