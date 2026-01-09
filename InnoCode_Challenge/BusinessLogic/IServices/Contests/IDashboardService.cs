using Repository.DTOs.DashboardDTOs;
using Utility.Enums;

namespace BusinessLogic.IServices.Contests
{
    public interface IDashboardService
    {
        Task<DashboardMetricsDTO> GetDashboardMetricsAsync(DateTime? startDate, DateTime? endDate, TimeRangePredefinedEnum? predefined);

    }
}
