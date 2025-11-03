using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Submissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.JudgeDTOs;
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
        /// <param name="problemIdSearch"></param>
        /// <param name="studentId"></param>
        /// <param name="teamName"></param>
        /// <param name="studentName"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetSubmissions(
            int pageNumber = 1, 
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? problemIdSearch = null,
            Guid? studentId = null,
            string? teamName = null,
            string? studentName = null)
        {
            PaginatedList<GetSubmissionDTO> result = await _submissionService.GetPaginatedSubmissionAsync(
                pageNumber, pageSize, idSearch, problemIdSearch, studentId, teamName, studentName);

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

        [HttpGet("{problemId}/result/me")]
        [Authorize(Roles = RoleConstants.Student)]
        public async Task<IActionResult> GetSubmissionResultOfLoggedInStudent([Required] Guid problemId)
        {
            GetSubmissionDTO result = await _submissionService.GetSubmissionResultOfLoggedInStudentAsync(problemId);
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
        /// <param name="submissionDTO"></param>
        /// <returns></returns>
        [HttpPost("evaluations")]
        [Authorize(Roles = RoleConstants.Student)]
        public async Task<IActionResult> EvaluateSubmission(CreateSubmissionDTO submissionDTO)
        {
            JudgeSubmissionResultDTO result = await _submissionService.EvaluateSubmissionAsync(submissionDTO);

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
        /// <param name="teamId">Team ID</param>
        /// <param name="problemId">Problem ID</param>
        /// <returns>Submission ID</returns>
        [HttpPost]
        [Route("files")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadFileSubmission(
            IFormFile file,
            [FromForm] Guid teamId,
            [FromForm] Guid problemId)
        {
            var submissionDTO = new CreateFileSubmissionDTO
            {
                TeamId = teamId,
                ProblemId = problemId
            };

            Guid submissionId = await _submissionService.CreateFileSubmissionAsync(submissionDTO, file);

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
        /// Update the score for a file submission (Judge only)
        /// </summary>
        /// <param name="submissionId">ID of the submission</param>
        /// <param name="scoreDTO">Score and feedback</param>
        /// <returns>Success message</returns>
        [HttpPut("{submissionId}/score")]
        [Authorize(Roles = RoleConstants.Judge)]
        public async Task<IActionResult> UpdateFileSubmissionScore(
            Guid submissionId,
            FileSubmissionScoreDTO scoreDTO)
        {
            bool result = await _submissionService.UpdateFileSubmissionScoreAsync(
                submissionId,
                scoreDTO.Score,
                scoreDTO.Feedback);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "File submission score updated successfully."
            ));
        }
    }
}
