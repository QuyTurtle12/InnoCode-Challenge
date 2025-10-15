using BusinessLogic.IServices.Mcqs;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqAttemptItemDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Mcqs
{
    [Route("api/mcq-attempt-items")]
    [ApiController]
    public class McqAttemptItemsController : ControllerBase
    {
        private readonly IMcqAttemptItemService _mcqAttemptItemService;

        // Constructor
        public McqAttemptItemsController(IMcqAttemptItemService mcqAttemptItemService)
        {
            _mcqAttemptItemService = mcqAttemptItemService;
        }

        /// <summary>
        /// Get paginated MCQ attempt items with optional filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PaginatedList<GetMcqAttemptItemDTO>>> GetPaginatedMcqAttemptItems(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? testIdSearch = null,
            Guid? questionIdSearch = null,
            string? testName = null,
            string? questionText = null)
        {
            PaginatedList<GetMcqAttemptItemDTO> result = await _mcqAttemptItemService.GetPaginatedMcqAttemptItemAsync(
                pageNumber,
                pageSize,
                idSearch,
                testIdSearch,
                questionIdSearch,
                testName,
                questionText);

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
                         message: "Mcq Attempt Item retrieved successfully."
                     ));
        }

        /// <summary>
        /// Create a new MCQ attempt item
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> CreateMcqAttemptItem([FromBody] CreateMcqAttemptItemDTO mcqAttemptItemDTO)
        {
            await _mcqAttemptItemService.CreateMcqAttemptItemAsync(mcqAttemptItemDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create mcq attempt item successfully."
                    ));
        }
    }
}
