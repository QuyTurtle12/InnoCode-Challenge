using Repository.DTOs.DashboardDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Dashboards
{
    public interface IOrganizerDashboardService
    {
        Task<OrganizerDashboardDTO> GetOrganizerDashboardAsync(
            Guid? organizerId,
            DateTime? startDate,
            DateTime? endDate,
            TimeRangePredefinedEnum? predefined);

        Task<ContestSummaryDTO> GetContestSummaryAsync(Guid contestId);

        Task<PaginatedList<ContestSummaryDTO>> GetMyContestsAsync(
            Guid? organizerId,
            int pageNumber,
            int pageSize,
            string? status,
            string? searchName,
            int? year,
            DateTime? startDate,
            DateTime? endDate);
    }
}
