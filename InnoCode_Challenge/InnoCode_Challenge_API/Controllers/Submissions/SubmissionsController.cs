using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Submissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Submissions
{
    [Route("api/[controller]")]
    [ApiController]
    public class SubmissionsController : ControllerBase
    {
        private readonly ISubmissionService _submissionService;

        // Constructor
        public SubmissionsController(ISubmissionService submissionService)
        {
            _submissionService = submissionService;
        }

        /// <summary>
        /// Gets a paginated list of submissions with optional filters
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="roundId"></param>
        /// <param name="studentId"></param>
        /// <param name="teamName"></param>
        /// <param name="studentName"></param>
        /// <returns></returns>
        [HttpGet("{roundId}")]
        public async Task<IActionResult> GetSubmissions(
            Guid roundId,
            int pageNumber = 1, 
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? studentId = null,
            string? teamName = null,
            string? studentName = null)
        {
            PaginatedList<GetSubmissionDTO> result = await _submissionService.GetPaginatedSubmissionAsync(
                pageNumber, pageSize, idSearch, roundId, studentId, teamName, studentName);

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
                        message: "Submission retrieved successfully."
                    ));
        }

        [HttpGet("{roundId}/result/me")]
        [Authorize(Roles = RoleConstants.Student)]
        public async Task<IActionResult> GetSubmissionResultOfLoggedInStudent([Required] Guid roundId)
        {
            GetSubmissionDTO result = await _submissionService.GetSubmissionResultOfLoggedInStudentAsync(roundId);
            return Ok(new BaseResponseModel<GetSubmissionDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Submission result retrieved successfully."
            ));
        }

        ///// <summary>
        ///// Creates a new submission
        ///// </summary>
        ///// <param name="submissionDto"></param>
        ///// <returns></returns>
        //[HttpPost]
        //public async Task<IActionResult> CreateSubmission(CreateSubmissionDTO submissionDto)
        //{
        //    await _submissionService.CreateSubmissionAsync(submissionDto);
        //    return Ok(new BaseResponseModel(
        //                 statusCode: StatusCodes.Status201Created,
        //                 code: ResponseCodeConstants.SUCCESS,
        //                 message: "Create Submission successfully."
        //             ));
        //}

        /// <summary>
        /// Updates an existing submission
        /// </summary>
        /// <param name="id"></param>
        /// <param name="submissionDto"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSubmission(Guid id, UpdateSubmissionDTO submissionDto)
        {
            await _submissionService.UpdateSubmissionAsync(id, submissionDto);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Submission successfully."
                    ));
        }

        /// <summary>
        /// Evaluates a submission using the Judge0 service
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="submissionDTO"></param>
        /// <returns></returns>
        [HttpPost("{roundId}/evaluations")]
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
        /// Upload a file submission (.zip or .rar)
        /// </summary>
        /// <param name="file">The file to upload (.zip or .rar)</param>
        /// <param name="roundId">Round ID</param>
        /// <returns>Submission ID</returns>
        [HttpPost]
        [Route("{roundId}/files")]
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
        /// Download a submitted file
        /// </summary>
        /// <param name="submissionId">ID of the submission</param>
        /// <returns>Download URL for the file</returns>
        [HttpGet("{submissionId}/download")]
        public async Task<IActionResult> GetFileDownloadUrl(Guid submissionId)
        {
            string downloadUrl = await _submissionService.GetFileSubmissionDownloadUrlAsync(submissionId);

            return Ok(new BaseResponseModel<string>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: downloadUrl,
                message: "File download URL retrieved successfully."
            ));
        }

        /// <summary>
        /// Accepts the result of a submission and adds the score to the team's leaderboard
        /// </summary>
        /// <param name="submissionId"></param>
        /// <returns></returns>
        [HttpPut("{submissionId}/acceptance")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> AcceptResult([Required] Guid submissionId)
        {
            await _submissionService.AddScoreToTeamInLeaderboardAsync(submissionId);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Score added to team in leaderboard successfully."
            ));
        }

        /// <summary>
        /// Submit rubric-based evaluation for a manual submission
        /// </summary>
        /// <param name="submissionId"></param>
        /// <param name="rubricScoreDTO">Rubric scores and feedback</param>
        /// <returns>Evaluation result with total score</returns>
        [HttpPost("{submissionId}/rubric-evaluation")]
        [Authorize(Policy = "RequireJudgeRole")]
        public async Task<IActionResult> SubmitRubricEvaluation(Guid submissionId, SubmitRubricScoreDTO rubricScoreDTO)
        {
            RubricEvaluationResultDTO result = await _submissionService.SubmitRubricEvaluationAsync(submissionId, rubricScoreDTO);

            return Ok(new BaseResponseModel<RubricEvaluationResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Rubric evaluation submitted successfully."
            ));
        }

        /// <summary>
        /// Get my manual test submission result (Student only)
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <returns>Manual test evaluation result for the logged-in student</returns>
        [HttpGet("rounds/{roundId}/manual-test/my-result")]
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
        [HttpGet("rounds/{roundId}/manual-test/results")]
        //[Authorize(Policy = "RequireOrganizerRole")]
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
    }
}
