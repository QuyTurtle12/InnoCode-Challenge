//using BusinessLogic.IServices.Submissions;
//using Microsoft.AspNetCore.Mvc;
//using Repository.DTOs.SubmissionDetailDTOs;
//using Repository.ResponseModel;
//using Utility.Constant;
//using Utility.PaginatedList;

//namespace InnoCode_Challenge_API.Controllers.Submissions
//{
//    [Route("api/[controller]")]
//    [ApiController]
//    public class SubmissionDetailsController : ControllerBase
//    {
//        private readonly ISubmissionDetailService _submissionDetailService;

//        // Constructor
//        public SubmissionDetailsController(ISubmissionDetailService submissionDetailService)
//        {
//            _submissionDetailService = submissionDetailService;
//        }

//        /// <summary>
//        /// Gets paginated submission details with optional filters
//        /// </summary>
//        /// <param name="pageNumber">Current page number</param>
//        /// <param name="pageSize">Number of items per page</param>
//        /// <param name="idSearch">Optional filter by detail ID</param>
//        /// <param name="submissionIdSearch">Optional filter by submission ID</param>
//        /// <param name="testcaseId">Optional filter by testcase ID</param>
//        /// <returns>Paginated list of submission details</returns>
//        [HttpGet]
//        public async Task<ActionResult<PaginatedList<GetSubmissionDetailDTO>>> GetSubmissionDetails(
//            int pageNumber = 1,
//            int pageSize = 10,
//            Guid? idSearch = null,
//            Guid? submissionIdSearch = null,
//            Guid? testcaseId = null)
//        {
//            PaginatedList<GetSubmissionDetailDTO> result = await _submissionDetailService.GetPaginatedSubmissionDetailAsync(
//                pageNumber, pageSize, idSearch, submissionIdSearch, testcaseId);

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
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        data: result.Items,
//                        additionalData: paging,
//                        message: "Submission Detail retrieved successfully."
//                    ));
//        }

//        /// <summary>
//        /// Creates a new submission detail
//        /// </summary>
//        /// <param name="submissionDetailDTO">Data for the new submission detail</param>
//        /// <returns>Action result indicating success or failure</returns>
//        [HttpPost]
//        public async Task<IActionResult> CreateSubmissionDetail(CreateSubmissionDetailDTO submissionDetailDTO)
//        {
//            await _submissionDetailService.CreateSubmissionDetailAsync(submissionDetailDTO);
//            return Ok(new BaseResponseModel(
//                         statusCode: StatusCodes.Status201Created,
//                         code: ResponseCodeConstants.SUCCESS,
//                         message: "Create Submission Detail successfully."
//                     ));
//        }

//        /// <summary>
//        /// Updates an existing submission detail
//        /// </summary>
//        /// <param name="id">ID of the submission detail to update</param>
//        /// <param name="submissionDetailDTO">Updated data</param>
//        /// <returns>Action result indicating success or failure</returns>
//        [HttpPut("{id}")]
//        public async Task<IActionResult> UpdateSubmissionDetail(Guid id, UpdateSubmissionDetailDTO submissionDetailDTO)
//        {
//            await _submissionDetailService.UpdateSubmissionDetailAsync(id, submissionDetailDTO);
//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Update Submission Detail successfully."
//                    ));
//        }

//        /// <summary>
//        /// Deletes a submission detail
//        /// </summary>
//        /// <param name="id">ID of the submission detail to delete</param>
//        /// <returns>Action result indicating success or failure</returns>
//        [HttpDelete("{id}")]
//        public async Task<IActionResult> DeleteSubmissionDetail(Guid id)
//        {
//            await _submissionDetailService.DeleteSubmissionDetailAsync(id);
//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Delete Submission Detail successfully."
//                    ));
//        }
//    }
//}
