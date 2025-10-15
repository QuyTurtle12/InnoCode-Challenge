using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.AttachmentDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AttachmentsController : ControllerBase
    {
        private readonly IAttachmentService _service;

        public AttachmentsController(IAttachmentService service) => _service = service;

        [HttpGet]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> Get([FromQuery] AttachmentQueryParams query)
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
                StatusCodes.Status200OK, ResponseCodeConstants.SUCCESS, page.Items, additional, "Attachments retrieved."
            ));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var data = await _service.GetByIdAsync(id);
            return Ok(BaseResponseModel<object>.OkResponseModel(data, "Attachment fetched."));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateAttachmentDTO dto)
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.AttachmentId },
                new BaseResponseModel<object>(StatusCodes.Status201Created, ResponseCodeConstants.SUCCESS, created, message: "Attachment created.")
            );
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAttachmentDTO dto)
        {
            var updated = await _service.UpdateAsync(id, dto);
            return Ok(BaseResponseModel<object>.OkResponseModel(updated,  "Attachment updated."));
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
