using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.DashboardDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;

namespace InnoCode_Challenge_API.Controllers.Dashboards
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardsController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardsController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        /// <summary>
        /// Get dashboard metrics such as total contests, teams, students, and contest status breakdown.
        /// </summary>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="predefined"></param>
        /// <returns></returns>
        [HttpGet("metrics")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetDashboardMetrics(
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = null)
        {

            DashboardMetricsDTO metrics = await _dashboardService.GetDashboardMetricsAsync(
                startDate,
                endDate,
                predefined);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: metrics,
                message: "Dashboard metrics retrieved successfully."));
        }
    }
}
