using System.ComponentModel.DataAnnotations;
using System.Net.NetworkInformation;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Mcqs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.BankDTOs;
using Repository.DTOs.QuizDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Mcqs
{
    [Route("api/quizzes")]
    [ApiController]
    public class QuizsController : ControllerBase
    {
        private readonly IQuizService _quizService;
        private readonly IConfigService _configService;

        public QuizsController(IQuizService quizService, IConfigService configService)
        {
            _quizService = quizService;
            _configService = configService;
        }

        /// <summary>
        /// Get quiz (MCQ Test) by round ID with pagination
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet("rounds/{roundId}/quiz")]
        public async Task<IActionResult> GetQuiz(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            GetQuizDTO quiz = await _quizService.GetQuizByRoundIdAsync(pageNumber, pageSize, roundId);

            return Ok(new BaseResponseModel<GetQuizDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: quiz,
                message: "Quiz retrieved successfully."
            ));
        }

        /// <summary>
        /// Submit answers for a quiz
        /// </summary>
        /// <param name="roundId">Round Id</param>
        /// <param name="submissionDTO">Quiz submission data</param>
        /// <returns>Quiz results</returns>
        [HttpPost("{roundId}/submit")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> SubmitQuiz(Guid roundId, CreateQuizSubmissionDTO submissionDTO)
        {
            QuizResultDTO result = await _quizService.ProcessQuizSubmissionAsync(roundId, submissionDTO);

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
        /// <param name="roundId">Required Round Id filter</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="studentId">Optional student ID filter</param>
        /// <param name="testId">Optional test ID filter</param>
        /// <returns>Paginated list of quiz attempt summaries</returns>
        [HttpGet("{roundId}/attempts")]
        public async Task<IActionResult> GetQuizAttempts(
            [Required] Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? studentId = null,
            Guid? testId = null
            )
        {
                PaginatedList<QuizAttemptSummaryDTO> result = await _quizService.GetStudentQuizAttemptsAsync(
                    pageNumber, pageSize, studentId, testId, roundId, false);

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
        /// <param name="roundId">Required Round Id filter</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="testId">Optional test ID filter</param>
        /// <returns>Paginated list of quiz attempt summaries</returns>
        [HttpGet("{roundId}/attempts/me")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> GetMyQuizAttempts(
            [Required] Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? testId = null)
        {
            PaginatedList<QuizAttemptSummaryDTO> result = await _quizService.GetStudentQuizAttemptsAsync(
                pageNumber, pageSize, null, testId, roundId, true);

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
        /// Gets paginated list of banks with their questions and options
        /// </summary>
        /// <param name="pageNumber">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 10)</param>
        /// <param name="bankId">Optional bank ID filter</param>
        /// <param name="nameSearch">Optional name search filter</param>
        /// <returns>Paginated list of banks</returns>
        [HttpGet("banks")]
        public async Task<IActionResult> GetPaginatedBanks(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? bankId = null,
            string? nameSearch = null)
        {
            var result = await _quizService.GetPaginatedBanksAsync(pageNumber, pageSize, bankId, nameSearch);

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
                message: "Banks retrieved successfully."
            ));
        }

        /// <summary>
        /// Import MCQ questions from CSV file
        /// </summary>
        /// <param name="csvFile">CSV file containing questions</param>
        /// <param name="testId">Test ID</param>
        /// <returns>Import result</returns>
        [HttpPost("/api/mcq-tests/{testId}/import-csv")]
        [Authorize(Policy = "RequireOrganizerRole")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportMcqQuestionsFromCsv(
            IFormFile csvFile,
            [FromRoute] Guid testId)
        {
            GetBankWithQuestionsDTO result = await _quizService.ImportMcqQuestionsFromCsvAsync(csvFile, testId);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Questions imported successfully."
            ));
        }

        /// <summary>
        /// Download MCQ import template
        /// </summary>
        /// <returns></returns>
        [HttpGet("/api/mcq-tests/template")]
        public async Task<IActionResult> DownloadMcqImportTemplate()
        {
            string url = await _configService.DownloadImportTemplate(ImportTemplateEnum.McqTemplate);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: url,
                message: "Template downloaded successfully."
            ));
        }
    }
}
