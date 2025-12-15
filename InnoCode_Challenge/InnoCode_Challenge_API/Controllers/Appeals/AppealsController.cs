using BusinessLogic.IServices.Appeals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.AppealDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Appeals
{
    [Route("api/")]
    [ApiController]
    public class AppealsController : ControllerBase
    {
        private readonly IAppealService _appealService;

        // Constructor
        public AppealsController(IAppealService appealService)
        {
            _appealService = appealService;
        }

        /// <summary>
        /// Create a new appeal for round retake (Mentor only)
        /// </summary>
        /// <param name="dto">Appeal creation data with evidences</param>
        /// <returns>Created appeal details</returns>
        [HttpPost("appeals")]
        [Authorize(Policy = "RequireMentorRole")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateAppeal([FromForm] CreateAppealDTO dto)
        {
            GetAppealDTO result = await _appealService.CreateAppealAsync(dto);

            return Ok(new BaseResponseModel<GetAppealDTO>(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Appeal created successfully."
            ));
        }

        /// <summary>
        /// Get appeal details by ID
        /// </summary>
        /// <param name="appealId">Appeal ID</param>
        /// <returns>Appeal details</returns>
        [HttpGet("appeals/{appealId}")]
        [Authorize]
        public async Task<IActionResult> GetAppealById(Guid appealId)
        {
            GetAppealDTO result = await _appealService.GetAppealByIdAsync(appealId);

            return Ok(new BaseResponseModel<GetAppealDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Appeal retrieved successfully."
            ));
        }

        /// <summary>
        /// Get paginated list of appeals with filters
        /// </summary>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="appealId">Optional appeal ID filter</param>
        /// <param name="teamId">Optional team ID filter</param>
        /// <param name="roundId">Optional round ID filter</param>
        /// <param name="contestId">Optional contest ID filter</param>
        /// <param name="state">Optional state filter (Opened/Closed)</param>
        /// <param name="decision">Optional decision filter (Approved/Rejected)</param>
        /// <returns>Paginated list of appeals</returns>
        [HttpGet("contests/{contestId}/appeals")]
        [Authorize]
        public async Task<IActionResult> GetPaginatedAppeals(
            Guid contestId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? appealId = null,
            Guid? teamId = null,
            Guid? roundId = null,
            AppealStateEnum? state = null,
            AppealDecisionEnum? decision = null)
        {
            PaginatedList<GetAppealDTO> result = await _appealService.GetPaginatedAppealsAsync(
                pageNumber, pageSize, appealId, contestId, teamId, roundId, state, decision, false);

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
                message: "Appeals retrieved successfully."
            ));
        }

        /// <summary>
        /// Get my appeals (current mentor's appeals)
        /// </summary>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="roundId">Optional round ID filter</param>
        /// <param name="contestId">Optional contest ID filter</param>
        /// <param name="state">Optional state filter (Opened/Closed)</param>
        /// <param name="decision">Optional decision filter (Approved/Rejected)</param>
        /// <returns>Paginated list of current mentor's appeals</returns>
        [HttpGet("contests/{contestId}/appeals/my-appeal")]
        [Authorize(Policy = "RequireMentorRole")]
        public async Task<IActionResult> GetMyAppeals(
            Guid contestId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? roundId = null,
            AppealStateEnum? state = null,
            AppealDecisionEnum? decision = null)
        {
            PaginatedList<GetAppealDTO> result = await _appealService.GetPaginatedAppealsAsync(
                pageNumber, pageSize, null, contestId, null, roundId, state, decision, true);

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
                message: "Appeals retrieved successfully."
            ));
        }

        /// <summary>
        /// Review an appeal (Organizer only) - Approve or Reject
        /// </summary>
        /// <param name="appealId">Appeal ID</param>
        /// <param name="dto">Review decision</param>
        /// <returns>Updated appeal details</returns>
        [HttpPut("appeals/{appealId}/review")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> ReviewAppeal(Guid appealId, ReviewAppealDTO dto)
        {
            GetAppealDTO result = await _appealService.ReviewAppealAsync(appealId, dto);

            return Ok(new BaseResponseModel<GetAppealDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Appeal reviewed successfully."
            ));
        }
    }
}
