using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ConfigDTOs;
using Repository.ResponseModel;
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
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Get([FromQuery] ConfigQueryParams query)
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
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, additional, "Configs retrieved."
            ));
        }

        [HttpGet("{key}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> GetByKey(string key)
        {
            var data = await _service.GetByKeyAsync(key);
            return Ok(BaseResponseModel<object>.OkResponseModel(data, "Config fetched."));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateConfigDTO dto)
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetByKey), new { key = created.Key },
                new BaseResponseModel<object>(StatusCodes.Status201Created, ResponseCodeConstants.SUCCESS, created, "Config created.")
            );
        }

        [HttpPut("{key}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(string key, [FromBody] UpdateConfigDTO dto)
        {
            var updated = await _service.UpdateAsync(key, dto);
            return Ok(BaseResponseModel<object>.OkResponseModel(updated,"Config updated."));
        }

        [HttpDelete("{key}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(string key)
        {
            await _service.DeleteAsync(key);
            return NoContent();
        }
    }
}
