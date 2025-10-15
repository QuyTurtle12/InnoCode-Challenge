using BusinessLogic.IServices.Certificates;
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

        // Constructor
        public CertificatesController(ICertificateService certificateService)
        {
            _certificateService = certificateService;
        }

        /// <summary>
        /// Get paginated list of certificates with optional search parameters
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="teamIdSearch"></param>
        /// <param name="studentIdSearch"></param>
        /// <param name="certificateNameSearch"></param>
        /// <param name="teamName"></param>
        /// <param name="studentNameSearch"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetCertificates(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? teamIdSearch = null,
            Guid? studentIdSearch = null,
            string? certificateNameSearch = null,
            string? teamName = null,
            string? studentNameSearch = null)
        {
            PaginatedList<GetCertificateDTO> result = await _certificateService.GetPaginatedCertificateAsync(
                pageNumber,
                pageSize,
                idSearch,
                teamIdSearch,
                studentIdSearch,
                certificateNameSearch,
                teamName,
                studentNameSearch);

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
                        message: "Certificate retrieved successfully."
                    ));
        }

        /// <summary>
        /// Create a new certificate
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateCertificate(CreateCertificateDTO certificate)
        {
            await _certificateService.CreateCertificateAsync(certificate);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create Certificate successfully."
                    ));
        }

        /// <summary>
        /// Delete a certificate by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCertificate(Guid id)
        {
            await _certificateService.DeleteCertificateAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Certificat successfully."
                    ));
        }
    }
}
