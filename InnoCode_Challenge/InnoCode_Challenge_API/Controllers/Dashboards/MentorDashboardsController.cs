using BusinessLogic.IServices.Dashboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.DashboardDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;

namespace InnoCode_Challenge_API.Controllers.Dashboards
{
    [Route("api/mentor-dashboards")]
    [ApiController]
    public class MentorDashboardController : ControllerBase
    {
        private readonly IMentorDashboardService _mentorDashboardService;

        public MentorDashboardController(IMentorDashboardService mentorDashboardService)
        {
            _mentorDashboardService = mentorDashboardService;
        }

        /// <summary>
        /// Get Mentor Dashboard
        /// </summary>
        /// <param name="mentorId"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="predefined"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrStaffOrMentor")]
        public async Task<IActionResult> GetMentorDashboard(
            Guid? mentorId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            TimeRangePredefinedEnum? predefined = null)
        {
            MentorDashboardDTO dashboard = await _mentorDashboardService.GetMentorDashboardAsync(
                mentorId,
                startDate,
                endDate,
                predefined);

            return Ok(new BaseResponseModel<MentorDashboardDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dashboard,
                message: "Mentor dashboard retrieved successfully."
            ));
        }
    }
}
