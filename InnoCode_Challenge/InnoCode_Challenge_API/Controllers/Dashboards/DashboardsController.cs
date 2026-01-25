using BusinessLogic.IServices.Dashboards;
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

        private const int DEFAULT_TOP_COUNT = 3;
        private const int DEFAULT_TOP_SCHOOL_COUNT = 5;

        public DashboardsController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        /// <summary>
        /// Get dashboard metrics
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
            TimeRangePredefinedEnum? predefined = TimeRangePredefinedEnum.AllTime)
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

        /// <summary>
        /// Get chart data
        /// </summary>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="predefined"></param>
        /// <returns></returns>
        [HttpGet("charts")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetChartData(
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum predefined = TimeRangePredefinedEnum.AllTime)
        {
            ChartDataDTO chartData = await _dashboardService.GetChartDataAsync(
                startDate,
                endDate,
                predefined);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: chartData,
                message: "Chart data retrieved successfully."));
        }

        /// <summary>
        /// Get top performers
        /// </summary>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="predefined"></param>
        /// <param name="topCount"></param>
        /// <returns></returns>
        [HttpGet("top-performers")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetTopPerformers(
            int topCount = DEFAULT_TOP_COUNT,
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = TimeRangePredefinedEnum.AllTime)
        {
            TopPerformersDTO topPerformers = await _dashboardService.GetTopPerformersAsync(
                startDate,
                endDate,
                predefined,
                topCount);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: topPerformers,
                message: "Top performers retrieved successfully."));
        }

        /// <summary>
        /// Get school metrics
        /// </summary>
        /// <param name="topSchoolCount"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="predefined"></param>
        /// <returns></returns>
        [HttpGet("school-metrics")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetSchoolMetrics(
            int topSchoolCount = DEFAULT_TOP_SCHOOL_COUNT,
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = TimeRangePredefinedEnum.AllTime)
        {
            SchoolMetricsDTO schoolMetrics = await _dashboardService.GetSchoolMetricsAsync(
                startDate,
                endDate,
                predefined,
                topSchoolCount);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: schoolMetrics,
                message: "School metrics retrieved successfully."));
        }
    }
}
