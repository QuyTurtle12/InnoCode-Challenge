using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.TestCaseDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/test-cases")]
    [ApiController]
    public class TestCasesController : ControllerBase
    {
        private readonly ITestCaseService _testCaseService;

        // Constructor
        public TestCasesController(ITestCaseService testCaseService)
        {
            _testCaseService = testCaseService;
        }

        /// <summary>
        /// Gets a paginated list of test cases with optional filters
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTestCases(int pageNumber = 1, int pageSize = 10, 
                                                     Guid? idSearch = null, Guid? problemIdSearch = null)
        {
            PaginatedList<GetTestCaseDTO> result = await _testCaseService.GetPaginatedTestCaseAsync(pageNumber, pageSize, idSearch, problemIdSearch);

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
                        message: "Test case retrieved successfully."
                    ));
        }

        /// <summary>
        /// Creates a new test case
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateTestCase([FromForm] CreateTestCaseDTO testCaseDto, IFormFile? inputFile, IFormFile? outputFile)
        {
            if (inputFile != null)
            {
                using var reader = new StreamReader(inputFile.OpenReadStream());
                testCaseDto.Input = await reader.ReadToEndAsync();
            }

            if (outputFile != null)
            {
                using var reader = new StreamReader(outputFile.OpenReadStream());
                testCaseDto.ExpectedOutput = await reader.ReadToEndAsync();
            }

            await _testCaseService.CreateTestCaseAsync(testCaseDto);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create Test Case successfully."
                    ));
        }

        /// <summary>
        /// Updates an existing test case
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTestCase(Guid id, [FromBody] UpdateTestCaseDTO testCaseDto)
        {
            await _testCaseService.UpdateTestCaseAsync(id, testCaseDto);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Test Case successfully."
                    ));
        }

        /// <summary>
        /// Deletes a test case by id
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTestCase(Guid id)
        {
            await _testCaseService.DeleteTestCaseAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Test Case successfully."
                    ));
        }
    }
}
