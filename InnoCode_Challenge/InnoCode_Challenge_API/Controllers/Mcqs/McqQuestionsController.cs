using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Mcqs;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqQuestionDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Mcqs
{
    [Route("api/mcq-questions")]
    [ApiController]
    public class McqQuestionsController : ControllerBase
    {
        private readonly IMcqQuestionService _mcqQuestionService;

        // Constructor
        public McqQuestionsController(IMcqQuestionService mcqQuestionService)
        {
            _mcqQuestionService = mcqQuestionService;
        }

        /// <summary>
        /// Gets paginated MCQ questions
        /// </summary>
        /// <param name="bankId">Required attribute to get a list of question</param>
        /// <param name="pageNumber">The page number to retrieve</param>
        /// <param name="pageSize">The number of items per page</param>
        /// <param name="idSearch">Optional GUID to filter questions</param>
        /// <returns>A paginated list of MCQ questions</returns>
        [HttpGet("{bankId}")]
        public async Task<ActionResult<PaginatedList<GetMcqQuestionDTO>>> GetMcqQuestions(
            [Required] Guid bankId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null)
        {
            PaginatedList<GetMcqQuestionDTO> result = await _mcqQuestionService.GetPaginatedMcqQuestionAsync(pageNumber, pageSize, idSearch, bankId);

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
                        message: "Questions retrieved successfully."
                    ));
        }
    }
}
