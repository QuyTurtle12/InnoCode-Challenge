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
    [Route("api/")]
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
        /// Get MCQ Test by round ID with pagination
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet("rounds/{roundId}/mcq-test")]
        public async Task<IActionResult> GetQuiz(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            GetQuizDTO quiz = await _quizService.GetQuizByRoundIdAsync(pageNumber, pageSize, roundId);

            var paging = new
            {
                quiz.McqTest?.CurrentPage,
                quiz.McqTest?.PageSize,
                quiz.McqTest?.TotalPages,
                TotalCount = quiz.McqTest?.TotalQuestions ?? 0,
                HasPreviousPage = (quiz.McqTest?.CurrentPage ?? 1) > 1,
                HasNextPage = (quiz.McqTest?.CurrentPage ?? 1) < (quiz.McqTest?.TotalPages ?? 0)
            };

            return Ok(new BaseResponseModel<GetQuizDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: quiz,
                additionalData: paging,
                message: "Quiz retrieved successfully."
            ));
        }

        /// <summary>
        /// Submit answers for a MCQ Test
        /// </summary>
        /// <param name="roundId">Round Id</param>
        /// <param name="submissionDTO">MCQ Test submission data</param>
        /// <returns>MCQ Test results</returns>
        [HttpPost("rounds/{roundId}/mcq-test/submit")]
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
        /// Get detailed results of a specific MCQ Test's attempt
        /// </summary>
        /// <param name="attemptId">ID of the MCQ Test attempt</param>
        /// <returns>Detailed MCQ Test results including answers</returns>
        [HttpGet("rounds/mcq-test/attempts/{attemptId}")]
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
        /// Get paginated list of all MCQ Test attempts
        /// </summary>
        /// <param name="roundId">Required Round Id filter</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="studentId">Optional student ID filter</param>
        /// <returns>Paginated list of MCQ Test attempt summaries</returns>
        [HttpGet("rounds/{roundId}/attempts")]
        public async Task<IActionResult> GetQuizAttempts(
            [Required] Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? studentId = null
            )
        {
            PaginatedList<QuizAttemptSummaryDTO> result = await _quizService.GetStudentQuizAttemptsAsync(
                pageNumber, pageSize, studentId, null, roundId, false);

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
        /// Get all MCQ Test attempts for the current student
        /// </summary>
        /// <param name="roundId">Required Round Id filter</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Paginated list of MCQ Test attempt summaries</returns>
        [HttpGet("rounds/{roundId}/attempts/my-attempt")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> GetMyQuizAttempts(
            [Required] Guid roundId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            PaginatedList<QuizAttemptSummaryDTO> result = await _quizService.GetStudentQuizAttemptsAsync(
                pageNumber, pageSize, null, null, roundId, true);

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
        [HttpPost("mcq-tests/{testId}/import-csv")]
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
        [HttpGet("mcq-tests/template")]
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

        /// <summary>
        /// Get MCQ Test start details by round ID
        /// </summary>
        /// <param name="roundId"></param>
        /// <returns></returns>
        [HttpGet("rounds/{roundId}/mcq-test/start-detail")]
        public async Task<IActionResult> GetMcqStartDetails(Guid roundId)
        {
            McqStartDTO result = await _quizService.GetMcqStartDetailsAsync(roundId);
            return Ok(new BaseResponseModel<McqStartDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "MCQ start details retrieved successfully."
            ));
        }

        /// <summary>
        /// Save current answers for MCQ Test
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="key"></param>
        /// <param name="saveAnswerDTO"></param>
        /// <returns></returns>
        [HttpPost("rounds/{roundId}/mcq-test/save-answer")]
        public async Task<IActionResult> SaveAnswers(
            Guid roundId,
            string key,
            List<CurrentAnswerDTO> saveAnswerDTO)
        {
            await _quizService.SaveAnswerAsync(key, saveAnswerDTO);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Answers saved successfully."
            ));
        }

        /// <summary>
        /// Get current answers for MCQ Test
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        [HttpGet("rounds/{roundId}/mcq-test/current-answer")]
        public async Task<IActionResult> GetCurrentAnswers(
            Guid roundId,
            string key)
        {
            SaveAnswerDTO result = await _quizService.GetCurrentAnswerAsync(key, roundId);
            return Ok(new BaseResponseModel<SaveAnswerDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Current answers retrieved successfully."
            ));
        }
    }
}
