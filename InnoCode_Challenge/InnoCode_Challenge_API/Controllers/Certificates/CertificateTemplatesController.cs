using BusinessLogic.IServices.Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.CertificateTemplateDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Certificates
{
    [Route("api/certificate-templates")]
    [ApiController]
    public class CertificateTemplatesController : ControllerBase
    {
        private readonly ICertificateTemplateService _certificateTemplateService;

        public CertificateTemplatesController(ICertificateTemplateService certificateTemplateService)
        {
            _certificateTemplateService = certificateTemplateService;
        }
        [HttpPost]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> Create([FromBody] CreateCertificateTemplateDTO dto)
        {
            var created = await _certificateTemplateService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.TemplateId },
                new BaseResponseModel<object>(
                    StatusCodes.Status201Created,
                    ResponseCodeConstants.SUCCESS,
                    created,
                    "Certificate template created."));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            CertificateTemplateDTO? dto = await _certificateTemplateService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                dto,
                "OK"));
        }

        [HttpGet]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> Get([FromQuery] Guid? contestId, [FromQuery] string? search,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = "createdAt", [FromQuery] bool desc = true)
        {
            var paged = await _certificateTemplateService.GetAsync(contestId, search, page, pageSize, sortBy, desc);
            var paging = new { paged.PageNumber, paged.PageSize, paged.TotalPages, paged.TotalCount, paged.HasPreviousPage, paged.HasNextPage };
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                paged.Items,
                paging,
                "OK"));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCertificateTemplateDTO dto)
        {
            var updated = await _certificateTemplateService.UpdateAsync(id, dto);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                updated,
                "Certificate template updated."));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            await _certificateTemplateService.SoftDeleteAsync(id);
            return Ok(new BaseResponseModel(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                "Certificate template deleted (soft)."));
        }

    }
}
