using BusinessLogic.IServices.Mcqs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.QuizDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Mcqs
{
    [Route("api/quizzes")]
    [ApiController]
    public class QuizsController : ControllerBase
    {
        private readonly IQuizService _quizService;

        public QuizsController(IQuizService quizService)
        {
            _quizService = quizService;
        }

        /// <summary>
        /// Get paginated quizzes by round ID
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet("{roundId}")]
        public async Task<IActionResult> GetQuizzesByRoundId(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            PaginatedList<GetQuizDTO> result = await _quizService.GetQuizByRoundIdAsync(pageNumber, pageSize, roundId);
            
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
                message: "Quizzes retrieved successfully."
            ));
        }

        /// <summary>
        /// Submit answers for a quiz
        /// </summary>
        /// <param name="submissionDTO">Quiz submission data</param>
        /// <returns>Quiz results</returns>
        [HttpPost("submit")]
        [Authorize(Roles = RoleConstants.Student)]
        public async Task<IActionResult> SubmitQuiz(CreateQuizSubmissionDTO submissionDTO)
        {
            QuizResultDTO result = await _quizService.ProcessQuizSubmissionAsync(submissionDTO);

            return Ok(new BaseResponseModel<QuizResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Quiz submitted and processed successfully."
            ));
        }

        /// <summary>
        /// Get detailed results of a specific quiz attempt
        /// </summary>
        /// <param name="attemptId">ID of the quiz attempt</param>
        /// <returns>Detailed quiz results including answers</returns>
        [HttpGet("attempts/{attemptId}")]
        public async Task<IActionResult> GetQuizAttemptResult(Guid attemptId)
        {
            QuizResultDTO result = await _quizService.GetQuizAttemptResultAsync(attemptId);

            return Ok(new BaseResponseModel<QuizResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Quiz attempt results retrieved successfully."
            ));
        }

        /// <summary>
        /// Get paginated list of all quiz attempts
        /// </summary>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="studentId">Optional student ID filter</param>
        /// <param name="testId">Optional test ID filter</param>
        /// <returns>Paginated list of quiz attempt summaries</returns>
        [HttpGet("attempts")]
        public async Task<IActionResult> GetQuizAttempts(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? studentId = null,
            Guid? testId = null)
        {
                PaginatedList<QuizAttemptSummaryDTO> result = await _quizService.GetStudentQuizAttemptsAsync(
                    pageNumber, pageSize, studentId, testId, false);

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
                    message: "Quiz attempts retrieved successfully."
                ));
        }

        /// <summary>
        /// Get all quiz attempts for the current student
        /// </summary>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="testId">Optional test ID filter</param>
        /// <returns>Paginated list of quiz attempt summaries</returns>
        [HttpGet("attempts/me")]
        [Authorize(Roles = RoleConstants.Student)]
        public async Task<IActionResult> GetMyQuizAttempts(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? testId = null)
        {
            PaginatedList<QuizAttemptSummaryDTO> result = await _quizService.GetStudentQuizAttemptsAsync(
                pageNumber, pageSize, null, testId, true);

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
                message: "Quiz attempts retrieved successfully."
            ));
        }
    }
}
