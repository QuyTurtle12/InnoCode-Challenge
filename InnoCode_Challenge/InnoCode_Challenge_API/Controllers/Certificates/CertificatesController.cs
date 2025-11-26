using BusinessLogic.IServices.Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.CertificateDTOs;
using Repository.ResponseModel;
using System.ComponentModel.DataAnnotations;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Certificates
{
    [Route("api/[controller]")]
    [ApiController]
    public class CertificatesController : ControllerBase
    {
        private readonly ICertificateService _certificateService;

        
        public CertificatesController(ICertificateService certificateService)
        {
            _certificateService = certificateService;
        }

        [HttpPost("issue")]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> Issue([FromBody] IssueCertificatesDTO dto)
        {
            var result = await _certificateService.IssueAsync(dto);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                result,
                "Certificates issued."));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var dto = await _certificateService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                dto,
                "OK"));
        }

        [HttpGet]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> Get([FromQuery] Guid? contestId, [FromQuery] Guid? templateId,
            [FromQuery] Guid? teamId, [FromQuery] Guid? studentId,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = "issuedAt", [FromQuery] bool desc = true)
        {
            var paged = await _certificateService.GetAsync(contestId, templateId, teamId, studentId, page, pageSize, sortBy, desc);
            var paging = new { paged.PageNumber, paged.PageSize, paged.TotalPages, paged.TotalCount, paged.HasPreviousPage, paged.HasNextPage };
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                paged.Items,
                paging,
                "OK"));
        }
    }
}
