using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
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

        /// <summary>
        /// Creates a new submission
        /// </summary>
        /// <param name="submissionDto"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateSubmission(CreateSubmissionDTO submissionDto)
        {
            await _submissionService.CreateSubmissionAsync(submissionDto);
            return Ok(new BaseResponseModel(
                         statusCode: StatusCodes.Status201Created,
                         code: ResponseCodeConstants.SUCCESS,
                         message: "Create Submission successfully."
                     ));
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
    }
}
