using BusinessLogic.IServices.Certificates;
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

        // Constructor
        public CertificateTemplatesController(ICertificateTemplateService certificateTemplateService)
        {
            _certificateTemplateService = certificateTemplateService;
        }

        /// <summary>
        /// Gets a paginated list of certificate templates
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCertificateTemplates(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? contestIdSearch = null,
            string? templateNameSearch = null,
            string? contestNameSearch = null)
        {
            PaginatedList<GetCertificateTemplateDTO> result = await _certificateTemplateService.GetPaginatedCertificateTemplateAsync(
                pageNumber, pageSize, idSearch, contestIdSearch, templateNameSearch, contestNameSearch);

            var paging = new
            {
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.TotalCount,
                result.HasPreviousPage,
                result.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result.Items,
                        additionalData: paging,
                        message: "Certificate templates retrieved successfully."
                    ));
        }

        /// <summary>
        /// Creates a new certificate template
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateCertificateTemplate(
            IFormFile file,
            [FromForm] Guid contestId,
            [FromForm] string name
            )
        {
            var templateDTO = new CreateCertificateTemplateDTO
            {
                ContestId = contestId,
                Name = name
            };

            await _certificateTemplateService.CreateCertificateTemplateAsync(file, templateDTO);

            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create Certificat template successfully."
                    ));
        }

        /// <summary>
        /// Updates an existing certificate template
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCertificateTemplate(
            Guid id,
            IFormFile? file,
            [FromForm] string? name)
        {
            UpdateCertificateTemplateDTO templateDTO = new UpdateCertificateTemplateDTO
            {
                Name = name!
            };

            await _certificateTemplateService.UpdateCertificateTemplateAsync(id, file, templateDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Certificat template successfully."
                    ));
        }

        /// <summary>
        /// Deletes a certificate template
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCertificateTemplate(Guid id)
        {
            await _certificateTemplateService.DeleteCertificateTemplateAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Certificat template successfully."
                    ));
        }
    }
}
