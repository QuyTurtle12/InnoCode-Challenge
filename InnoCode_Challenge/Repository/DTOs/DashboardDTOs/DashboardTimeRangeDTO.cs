using Utility.Enums;

namespace Repository.DTOs.DashboardDTOs
{
    public class DashboardTimeRangeDTO
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public TimeRangePredefinedEnum? Predefined { get; set; }
    }
}
