//using BusinessLogic.IServices.Mcqs;
//using Microsoft.AspNetCore.Mvc;
//using Repository.DTOs.McqTestQuestionDTOs;
//using Repository.ResponseModel;
//using Utility.Constant;
//using Utility.PaginatedList;

//namespace InnoCode_Challenge_API.Controllers.Mcqs
//{
//    [Route("api/mcq-test-questions")]
//    [ApiController]
//    public class McqTestQuestionsController : ControllerBase
//    {
//        private readonly IMcqTestQuestionService _mcqTestQuestionService;

//        // Constructor
//        public McqTestQuestionsController(IMcqTestQuestionService mcqTestQuestionService)
//        {
//            _mcqTestQuestionService = mcqTestQuestionService;
//        }

//        /// <summary>
//        /// Gets paginated MCQ test questions with optional filters
//        /// </summary>
//        [HttpGet]
//        public async Task<IActionResult> GetPaginatedTestQuestions(
//            int pageNumber = 1,
//            int pageSize = 10,
//            Guid? testIdSearch = null,
//            Guid? questionIdSearch = null)
//        {
//            PaginatedList<GetMcqTestQuestionDTO> result = await _mcqTestQuestionService.GetPaginatedTestQuestionAsync(
//                pageNumber, pageSize, testIdSearch, questionIdSearch);

//            var paging = new
//            {
//                result.PageNumber,
//                result.PageSize,
//                result.TotalPages,
//                result.TotalCount,
//                result.HasPreviousPage,
//                result.HasNextPage
//            };

//            return Ok(new BaseResponseModel<object>(
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        data: result.Items,
//                        additionalData: paging,
//                        message: "Test Questions retrieved successfully."
//                    ));
//        }

//        /// <summary>
//        /// Creates a new MCQ test question
//        /// </summary>
//        [HttpPost]
//        public async Task<IActionResult> CreateTestQuestion(CreateMcqTestQuestionDTO createTestQuestionDTO)
//        {
//            await _mcqTestQuestionService.CreateTestQuestionAsync(createTestQuestionDTO);

//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status201Created,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Create Test Questions successfully."
//                    ));
//        }

//        /// <summary>
//        /// Updates an existing MCQ test question
//        /// </summary>
//        [HttpPut("{id}")]
//        public async Task<IActionResult> UpdateTestQuestion(
//            Guid id,
//            UpdateMcqTestQuestionDTO updateTestQuestionDTO)
//        {
//            await _mcqTestQuestionService.UpdateTestQuestionAsync(id, updateTestQuestionDTO);

//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Update Test Question successfully."
//                    ));
//        }

//        /// <summary>
//        /// Deletes an MCQ test question
//        /// </summary>
//        [HttpDelete("{id}")]
//        public async Task<IActionResult> DeleteTestQuestion(Guid id)
//        {
//            await _mcqTestQuestionService.DeleteTestQuestionAsync(id);

//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Delete Test Question successfully."
//                    ));
//        }
//    }
//}
