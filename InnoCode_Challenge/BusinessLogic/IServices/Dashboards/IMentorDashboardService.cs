using Repository.DTOs.DashboardDTOs;
using Utility.Enums;

namespace BusinessLogic.IServices.Dashboards
{
    public interface IMentorDashboardService
    {
        Task<MentorDashboardDTO> GetMentorDashboardAsync(
            Guid? mentorId,
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined);
    }
}
