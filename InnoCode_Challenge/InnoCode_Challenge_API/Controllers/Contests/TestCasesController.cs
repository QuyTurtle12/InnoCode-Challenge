using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.TestCaseDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/")]
    [ApiController]
    public class TestCasesController : ControllerBase
    {
        private readonly ITestCaseService _testCaseService;
        private readonly IConfigService _configService;

        // Constructor
        public TestCasesController(ITestCaseService testCaseService, IConfigService configService)
        {
            _testCaseService = testCaseService;
            _configService = configService;
        }

        /// <summary>
        /// Get paginated test cases for a specific round
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet("rounds/{roundId}/test-cases")]
        public async Task<IActionResult> GetTestCasesByRoundId(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            PaginatedList<GetTestCaseDTO> result = await _testCaseService.GetTestCasesByRoundIdAsync(
                roundId, pageNumber, pageSize);

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
                message: "Test cases retrieved successfully."
            ));
        }

        /// <summary>
        /// Create a new test case for a round
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="testCaseDTO"></param>
        /// <returns></returns>
        [HttpPost("rounds/{roundId}/test-cases")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> CreateTestCase(Guid roundId, CreateTestCaseDTO testCaseDTO)
        {
            await _testCaseService.CreateTestCaseAsync(roundId, testCaseDTO);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                message: "Test case created successfully."
            ));
        }

        /// <summary>
        /// Bulk update multiple test cases for a round
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="testCaseDTOs"></param>
        /// <returns></returns>
        [HttpPut("rounds/{roundId}/test-cases")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> BulkUpdateTestCases(Guid roundId, IList<BulkUpdateTestCaseDTO> testCaseDTOs)
        {
            await _testCaseService.BulkUpdateTestCasesAsync(roundId, testCaseDTOs);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: $"Successfully updated {testCaseDTOs.Count} test case(s)."
            ));
        }

        /// <summary>
        /// Delete a test case by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("rounds/{roundId}/test-cases/{id}")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> DeleteTestCase(Guid id)
        {
            await _testCaseService.DeleteTestCaseAsync(id);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Test case deleted successfully."
            ));
        }

        [HttpPost("rounds/{roundId}/test-cases/import-csv")]
        [Authorize(Policy = "RequireOrganizerRole")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportTestCasesFromCsv(
            IFormFile csvFile,
            [FromRoute] Guid roundId)
        {
            var result = await _testCaseService.ImportTestCasesFromCsvAsync(csvFile, roundId);

            return Ok(new BaseResponseModel<TestCaseImportResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: $"Import completed. {result.SuccessCount} test cases imported to round '{result.RoundName}'"
            ));
        }

        /// <summary>
        /// Download test case import template
        /// </summary>
        /// <returns></returns>
        [HttpGet("/api/test-cases/template")]
        public async Task<IActionResult> DownloadMcqImportTemplate()
        {
            string url = await _configService.DownloadImportTemplate(ImportTemplateEnum.TestCaseTemplate);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: url,
                message: "Template downloaded successfully."
            ));
        }
    }
}
