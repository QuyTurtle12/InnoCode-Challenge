using Repository.DTOs.DashboardDTOs;
using Utility.Enums;

namespace BusinessLogic.IServices.Dashboards
{
    public interface IDashboardService
    {
        Task<DashboardMetricsDTO> GetDashboardMetricsAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined);

        Task<ChartDataDTO> GetChartDataAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined);

        Task<TopPerformersDTO> GetTopPerformersAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined,
            int topCount);

        Task<SchoolMetricsDTO> GetSchoolMetricsAsync(
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined,
            int topSchoolCount);
    }
}
