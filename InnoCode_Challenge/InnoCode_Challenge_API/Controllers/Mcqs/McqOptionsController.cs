using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqOptionDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Mcqs
{
    [Route("api/mcq-options")]
    [ApiController]
    public class McqOptionsController : ControllerBase
    {
        private readonly IMcqOptionService _mcqOptionService;

        // Constructor
        public McqOptionsController(IMcqOptionService mcqOptionService)
        {
            _mcqOptionService = mcqOptionService;
        }

        /// <summary>
        /// Gets paginated MCQ options
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPaginatedMcqOptions(int pageNumber = 1, int pageSize = 10, Guid? id = null, Guid? questionId = null)
        {
            PaginatedList<GetMcqOptionDTO> result = await _mcqOptionService.GetPaginatedMcqOptionAsync(pageNumber, pageSize, id, questionId);

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
                        message: "Option retrieved successfully."
                    ));
        }

        /// <summary>
        /// Creates a new MCQ option
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateMcqOption(CreateMcqOptionDTO mcqOptionDTO)
        {
            await _mcqOptionService.CreateMcqOptionAsync(mcqOptionDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create mcq option successfully."
                    ));
        }

        /// <summary>
        /// Updates an existing MCQ option
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMcqOption(Guid id, UpdateMcqOptionDTO mcqOptionDTO)
        {
            await _mcqOptionService.UpdateMcqOptionAsync(id, mcqOptionDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update mcq option successfully."
                    ));
        }

        /// <summary>
        /// Deletes an MCQ option
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMcqOption(Guid id)
        {
            await _mcqOptionService.DeleteMcqOptionAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete mcq option successfully."
                    ));
        }
    }
}
