using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Submissions
{
    [Route("api/[controller]")]
    [ApiController]
    public class SubmissionArtifactsController : ControllerBase
    {
        private readonly ISubmissionArtifactService _submissionArtifactService;

        // Constructor
        public SubmissionArtifactsController(ISubmissionArtifactService submissionArtifactService)
        {
            _submissionArtifactService = submissionArtifactService;
        }

        /// <summary>
        /// Get paginated list of Submission Artifacts with optional search
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="submissionIdSearch"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetSubmissionArtifacts(int pageNumber = 1, int pageSize = 10, Guid? idSearch = null, Guid? submissionIdSearch = null)
        {
            PaginatedList<GetSubmissionArtifactDTO> result = await _submissionArtifactService.GetPaginatedSubmissionArtifactAsync(pageNumber, pageSize, idSearch, submissionIdSearch);

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
                        message: "Submission Artifact retrieved successfully."
                    ));
        }

        /// <summary>
        /// Create a new Submission Artifact
        /// </summary>
        /// <param name="submissionArtifactDTO"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateSubmissionArtifact(CreateSubmissionArtifactDTO submissionArtifactDTO)
        {
            await _submissionArtifactService.CreateSubmissionArtifactAsync(submissionArtifactDTO);
            return Ok(new BaseResponseModel(
                         statusCode: StatusCodes.Status201Created,
                         code: ResponseCodeConstants.SUCCESS,
                         message: "Create Submission Artifact successfully."
                     ));
        }

        /// <summary>
        /// Update an existing Submission Artifact
        /// </summary>
        /// <param name="id"></param>
        /// <param name="submissionArtifactDTO"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSubmissionArtifact(Guid id, UpdateSubmissionArtifactDTO submissionArtifactDTO)
        {
            await _submissionArtifactService.UpdateSubmissionArtifactAsync(id, submissionArtifactDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Submission Artifact successfully."
                    ));
        }

        /// <summary>
        /// Delete a Submission Artifact by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSubmissionArtifact(Guid id)
        {
            await _submissionArtifactService.DeleteSubmissionArtifactAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Submission Artifact successfully."
                    ));
        }
    }
}
