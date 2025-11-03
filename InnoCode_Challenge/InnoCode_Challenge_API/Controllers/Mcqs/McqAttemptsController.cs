//using BusinessLogic.IServices.Mcqs;
//using Microsoft.AspNetCore.Mvc;
//using Repository.DTOs.McqAttemptDTOs;
//using Repository.ResponseModel;
//using Utility.Constant;
//using Utility.PaginatedList;

//namespace InnoCode_Challenge_API.Controllers.Mcqs
//{
//    [Route("api/mcq-attempts")]
//    [ApiController]
//    public class McqAttemptsController : ControllerBase
//    {
//        private readonly IMcqAttemptService _mcqAttemptService;

//        // Constructor
//        public McqAttemptsController(IMcqAttemptService mcqAttemptService)
//        {
//            _mcqAttemptService = mcqAttemptService;
//        }

//        /// <summary>
//        /// Gets a paginated list of MCQ attempts with optional filtering
//        /// </summary>
//        /// <param name="pageNumber">Page number (starts from 1)</param>
//        /// <param name="pageSize">Number of items per page</param>
//        /// <param name="attemptId">Optional filter by attempt ID</param>
//        /// <param name="testId">Optional filter by test ID</param>
//        /// <param name="roundId">Optional filter by round ID</param>
//        /// <param name="studentId">Optional filter by student ID</param>
//        /// <param name="testName">Optional filter by test name</param>
//        /// <param name="roundName">Optional filter by round name</param>
//        /// <param name="studentName">Optional filter by student name</param>
//        /// <returns>Paginated list of MCQ attempts</returns>
//        [HttpGet]
//        public async Task<IActionResult> GetMcqAttempts(
//            int pageNumber = 1, 
//            int pageSize = 10,
//            Guid? attemptId = null,
//            Guid? testId = null,
//            Guid? roundId = null,
//            Guid? studentId = null,
//            string? testName = null,
//            string? roundName = null,
//            string? studentName = null)
//        {
//            PaginatedList<GetMcqAttemptDTO> result = await _mcqAttemptService.GetPaginatedMcqAttemptAsync(
//                pageNumber, pageSize, attemptId, testId, roundId, 
//                studentId, testName, roundName, studentName);

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
//                         statusCode: StatusCodes.Status200OK,
//                         code: ResponseCodeConstants.SUCCESS,
//                         data: result.Items,
//                         message: "Mcq Attempt retrieved successfully."
//                     ));
//        }

//        /// <summary>
//        /// Creates a new MCQ attempt
//        /// </summary>
//        /// <param name="mcqAttemptDTO">The MCQ attempt data</param>
//        /// <returns>Status code indicating result</returns>
//        [HttpPost]
//        public async Task<IActionResult> CreateMcqAttempt([FromBody] CreateMcqAttemptDTO mcqAttemptDTO)
//        {
//            await _mcqAttemptService.CreateMcqAttemptAsync(mcqAttemptDTO);

//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status201Created,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Create mcq attempt successfully."
//                    ));
//        }
//    }
//}
