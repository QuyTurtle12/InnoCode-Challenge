using BusinessLogic.IServices.Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.CertificateDTOs;
using Repository.ResponseModel;
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
        public async Task<IActionResult> Issue(IssueCertificatesDTO dto)
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
            CertificateDTO? dto = await _certificateService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                dto,
                "OK"));
        }

        [HttpGet]
        [Authorize(Policy = "RequireOrganizerOrAdmin")]
        public async Task<IActionResult> Get(
            Guid? contestId,
            Guid? templateId,
            Guid? teamId,
            Guid? studentId,
            int page = 1,
            int pageSize = 20,
            string? sortBy = "issuedAt",
            bool desc = true)
        {
            PaginatedList<CertificateDTO> paged = await _certificateService.GetAsync(contestId, templateId, teamId, studentId, page, pageSize, sortBy, desc, false);
            
            var paging = new { paged.PageNumber, paged.PageSize, paged.TotalPages, paged.TotalCount, paged.HasPreviousPage, paged.HasNextPage };
            
            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                paged.Items,
                paging,
                "OK"));
        }

        [HttpGet("my-certificate")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> GetMyCertificate(
            Guid? contestId,
            Guid? templateId,
            Guid? teamId,
            Guid? studentId,
            int page = 1,
            int pageSize = 20,
            string? sortBy = "issuedAt",
            bool desc = true)
        {
            PaginatedList<CertificateDTO> paged = await _certificateService.GetAsync(contestId, templateId, teamId, studentId, page, pageSize, sortBy, desc, true);
            
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
