using BusinessLogic.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqQuestionDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
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
        /// <param name="pageNumber">The page number to retrieve</param>
        /// <param name="pageSize">The number of items per page</param>
        /// <param name="idSearch">Optional GUID to filter questions</param>
        /// <returns>A paginated list of MCQ questions</returns>
        [HttpGet]
        public async Task<ActionResult<PaginatedList<GetMcqQuestionDTO>>> GetMcqQuestions(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null)
        {
            try
            {
                PaginatedList<GetMcqQuestionDTO> result = await _mcqQuestionService.GetPaginatedMcqQuestionAsync(pageNumber, pageSize, idSearch);
                return Ok(new BaseResponseModel<PaginatedList<GetMcqQuestionDTO>>(
                            statusCode: StatusCodes.Status200OK,
                            code: ResponseCodeConstants.SUCCESS,
                            data: result,
                            message: "Questions retrieved successfully."
                        ));
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }
    }
}
