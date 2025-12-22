using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Submissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
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
            await _submissionService.AcceptResultAsync(submissionId);
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

        [HttpGet()]
        [Authorize(Policy = "RequireJudgeRole")]
        public async Task<IActionResult> GetSubmissionsTest(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? contestIdSearch = null,
            string? contestName = null,
            Guid? roundIdSearch = null,
            string? roundName = null,
            Guid? teamIdSearch = null,
            string? teamName = null,
            Guid? studentIdSearch = null,
            string? studentName = null,
            SubmissionStatusEnum? statusFilter = null
            )
        {
            var result = await _submissionService.GetSubmissionsByJudgeByAsync(
                pageNumber,
                pageSize,
                contestIdSearch,
                contestName,
                roundIdSearch,
                roundName,
                teamIdSearch,
                teamName,
                studentIdSearch,
                studentName,
                statusFilter
                );

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

        /// <summary>
        /// Gets submission details by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSubmissionById(Guid id)
        {
            SubmissionDistributionDTO result = await _submissionService.GetSubmissionByIdAsync(id);
            return Ok(new BaseResponseModel<SubmissionDistributionDTO>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result,
                        message: "Submission retrieved successfully."
                    ));
        }

        /// <summary>
        /// Create null auto-evaluation submission (accept 0 points and finish round)
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <returns>Submission result with 0 score</returns>
        [HttpPost("/api/rounds/{roundId}/auto-evaluation/null-submission")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> CreateNullAutoSubmission(Guid roundId)
        {
            JudgeSubmissionResultDTO result = await _submissionService.CreateNullAutoSubmissionAsync(roundId);
            return Ok(new BaseResponseModel<JudgeSubmissionResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Null auto-evaluation submission accepted with 0 points."
            ));
        }

        /// <summary>
        /// Create null manual submission (accept 0 points and finish round)
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <returns>Submission ID</returns>
        [HttpPost("/api/rounds/{roundId}/manual/null-submission")]
        [Authorize(Policy = "RequireStudentRole")]
        public async Task<IActionResult> CreateNullManualSubmission(Guid roundId)
        {
            Guid submissionId = await _submissionService.CreateNullManualSubmissionAsync(roundId);
            return Ok(new BaseResponseModel<Guid>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: submissionId,
                message: "Null manual submission accepted with 0 points and round marked as finished."
            ));
        }
    }
}
