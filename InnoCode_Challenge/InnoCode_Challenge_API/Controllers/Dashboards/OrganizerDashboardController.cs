using BusinessLogic.IServices.Dashboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.DashboardDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Dashboards
{
    [Route("api/organizer-dashboards")]
    [ApiController]
    public class OrganizerDashboardController : ControllerBase
    {
        private readonly IOrganizerDashboardService _organizerDashboardService;

        public OrganizerDashboardController(IOrganizerDashboardService organizerDashboardService)
        {
            _organizerDashboardService = organizerDashboardService;
        }

        /// <summary>
        /// Get organizer dashboard metrics
        /// </summary>
        /// <param name="organizerId"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="predefined"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrStaffOrOrganizer")]
        public async Task<IActionResult> GetOrganizerDashboard(
             Guid? organizerId = null,
             DateTime? startDate = null,
             DateTime? endDate = null,
             TimeRangePredefinedEnum? predefined = TimeRangePredefinedEnum.LastYear)
        {
            OrganizerDashboardDTO dashboard = await _organizerDashboardService.GetOrganizerDashboardAsync(
                organizerId,
                startDate,
                endDate,
                predefined);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dashboard,
                message: "Organizer dashboard retrieved successfully."
            ));
        }

        /// <summary>
        /// Get paginated list of contests for the organizer
        /// </summary>
        /// <param name="organizerId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="status"></param>
        /// <param name="searchName"></param>
        /// <param name="year"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <returns></returns>
        [HttpGet("contests")]
        [Authorize(Policy = "RequireAdminOrStaffOrOrganizer")]
        public async Task<IActionResult> GetMyContests(
            Guid? organizerId = null,
            int pageNumber = 1,
            int pageSize = 10,
            string? status = null,
            string? searchName = null,
            int? year = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            PaginatedList<ContestSummaryDTO> contests = await _organizerDashboardService.GetMyContestsAsync(
                organizerId,
                pageNumber,
                pageSize,
                status,
                searchName,
                year,
                startDate,
                endDate);

            var paging = new
            {
                contests.PageNumber,
                contests.PageSize,
                contests.TotalCount,
                contests.TotalPages,
                contests.HasNextPage,
                contests.HasPreviousPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: contests.Items,
                additionalData: paging,
                message: "Contests retrieved successfully."
            ));
        }

        /// <summary>
        /// Get detailed metrics for a specific contest
        /// </summary>
        /// <param name="contestId">Contest ID</param>
        /// <returns></returns>
        [HttpGet("contests/{contestId}")]
        [Authorize(Policy = "RequireAdminOrStaffOrOrganizer")]
        public async Task<IActionResult> GetContestSummary(Guid contestId)
        {
            ContestSummaryDTO summary = await _organizerDashboardService.GetContestSummaryAsync(contestId);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: summary,
                message: "Contest summary retrieved successfully."
            ));
        }
    }
}
