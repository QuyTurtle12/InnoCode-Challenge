using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Submissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.DTOs.TestCaseDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoundsController : ControllerBase
    {
        private readonly IRoundService _roundService;
        private readonly ISubmissionService _submissionService;

        // Constructor
        public RoundsController(IRoundService roundService, ISubmissionService submissionService)
        {
            _roundService = roundService;
            _submissionService = submissionService;
        }

        /// <summary>
        /// Get paginated rounds with optional filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PaginatedList<GetRoundDTO>>> GetRounds(
             int pageNumber = 1,
             int pageSize = 10,
             Guid? idSearch = null,
             Guid? contestIdSearch = null,
             string? roundNameSearch = null,
             string? contestNameSearch = null,
             DateTime? startDate = null,
             DateTime? endDate = null)
        {
            PaginatedList<GetRoundDTO> result = await _roundService.GetPaginatedRoundAsync(pageNumber, pageSize, idSearch, contestIdSearch, roundNameSearch, contestNameSearch, startDate, endDate);

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
                        message: "Round retrieved successfully."
                    ));
        }

        /// <summary>
        /// Create a new round
        /// </summary>
        [HttpPost("{contestId}")]
        public async Task<IActionResult> CreateRound(Guid contestId, CreateRoundDTO roundDTO)
        {
            await _roundService.CreateRoundAsync(contestId, roundDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create round successfully."
                    ));
        }

        /// <summary>
        /// Update an existing round
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRound(Guid id, UpdateRoundDTO roundDTO)
        {
            await _roundService.UpdateRoundAsync(id, roundDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update round successfully."
                    ));
        }

        /// <summary>
        /// Delete a round by id
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRound(Guid id)
        {
            await _roundService.DeleteRoundAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete round successfully."
                    ));
        }

        /// <summary>
        /// Get manual type submissions by round id with pagination and optional status filter
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="statusFilter">Pending, Finished, Cancelled</param>
        /// <returns></returns>
        [HttpGet("{roundId}/submissions/manual-type")]
        public async Task<IActionResult> GetManualTypeSubmissionsByRoundId(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            SubmissionStatusEnum? statusFilter = SubmissionStatusEnum.Pending
            )
        {
            var submissions = await _roundService.GetManualTypeSubmissionsByRoundId(pageNumber, pageSize, roundId, statusFilter);

            var paging = new
            {
                submissions.PageNumber,
                submissions.PageSize,
                submissions.TotalPages,
                submissions.TotalCount,
                submissions.HasPreviousPage,
                submissions.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: submissions.Items,
                        additionalData: paging,
                        message: "Manual type submissions retrieved successfully."
                    ));
        }

            
        [HttpGet("{id}/time-limit")]
        public async Task<IActionResult> GetRoundTimeLimit(Guid id)
        {
            int? timeLimitSeconds = await _roundService.GetRoundTimeLimitSecondsAsync(id);

            return Ok(new BaseResponseModel<object>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: timeLimitSeconds,
                        message: "Round time limit retrieved successfully."
                    ));
        }

        /// <summary>
        /// Get my manual test submission result (Student only)
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <returns>Manual test evaluation result for the logged-in student</returns>
        [HttpGet("{roundId}/manual-test/my-result")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> GetMyManualTestResult(Guid roundId)
        {
            RubricEvaluationResultDTO result = await _submissionService.GetMyManualTestResultAsync(roundId);

            return Ok(new BaseResponseModel<RubricEvaluationResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Manual test result retrieved successfully."
            ));
        }

        /// <summary>
        /// Get all manual test results for a round with pagination and optional filters (Organizer only)
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="studentIdSearch"></param>
        /// <param name="teamIdSearch"></param>
        /// <param name="studentNameSearch"></param>
        /// <param name="teamNameSearch"></param>
        /// <returns></returns>
        [HttpGet("{roundId}/manual-test/results")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> GetAllManualTestResults(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? studentIdSearch = null,
            Guid? teamIdSearch = null,
            string? studentNameSearch = null,
            string? teamNameSearch = null)
        {
            PaginatedList<RubricEvaluationResultDTO> results = await _submissionService
                .GetAllManualTestResultsByRoundAsync(roundId, pageNumber, pageSize, studentIdSearch, teamIdSearch, studentNameSearch, teamNameSearch);

            var paging = new
            {
                results.PageNumber,
                results.PageSize,
                results.TotalPages,
                results.TotalCount,
                results.HasPreviousPage,
                results.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: results.Items,
                additionalData: paging,
                message: "Manual test results retrieved successfully."
            ));
        }

        /// <summary>
        /// Upload a file submission (.zip or .rar)
        /// </summary>
        /// <param name="file">The file to upload (.zip or .rar)</param>
        /// <param name="roundId">Round ID</param>
        /// <returns>Submission ID</returns>
        [HttpPost]
        [Route("{roundId}/manual-test/submissions")]
        [Consumes("multipart/form-data")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> UploadFileSubmission(
            [FromRoute] Guid roundId,
            IFormFile file
            )
        {
            Guid submissionId = await _submissionService.CreateFileSubmissionAsync(roundId, file);

            return Ok(new BaseResponseModel<Guid>(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                data: submissionId,
                message: "File submission created successfully."
            ));
        }

        /// <summary>
        /// Evaluates a submission using the Judge0 service
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="submissionDTO"></param>
        /// <returns></returns>
        [HttpPost("{roundId}/auto-test/submissions")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> EvaluateSubmission(Guid roundId, CreateSubmissionDTO submissionDTO)
        {
            JudgeSubmissionResultDTO result = await _submissionService.EvaluateSubmissionAsync(roundId, submissionDTO);

            return Ok(new BaseResponseModel<JudgeSubmissionResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Submission evaluated successfully."
            ));
        }

        /// <summary>
        /// Get auto test result for the logged-in student in a specific round
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <returns>Auto test submission result</returns>
        [HttpGet("{roundId}/auto-test/my-result")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> GetMyAutoTestResult(Guid roundId)
        {
            GetSubmissionDTO result = await _submissionService.GetMyAutoTestResultAsync(roundId);

            return Ok(new BaseResponseModel<GetSubmissionDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Auto test result retrieved successfully."
            ));
        }

        /// <summary>
        /// Get all auto test results by round with pagination and optional filters
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <param name="pageNumber">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 10)</param>
        /// <param name="studentIdSearch">Filter by student ID</param>
        /// <param name="teamIdSearch">Filter by team ID</param>
        /// <param name="studentNameSearch">Filter by student name</param>
        /// <param name="teamNameSearch">Filter by team name</param>
        /// <returns>Paginated list of auto test results</returns>
        [HttpGet("{roundId}/auto-test/results")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> GetAllAutoTestResultsByRound(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? studentIdSearch = null,
            Guid? teamIdSearch = null,
            string? studentNameSearch = null,
            string? teamNameSearch = null)
        {
            PaginatedList<GetSubmissionDTO> results = await _submissionService.GetAllAutoTestResultsByRoundAsync(
                roundId,
                pageNumber,
                pageSize,
                studentIdSearch,
                teamIdSearch,
                studentNameSearch,
                teamNameSearch);

            var paging = new
            {
                results.PageNumber,
                results.PageSize,
                results.TotalPages,
                results.TotalCount,
                results.HasPreviousPage,
                results.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: results.Items,
                additionalData: paging,
                message: "Auto test results retrieved successfully."
            ));
        }

    }
}
