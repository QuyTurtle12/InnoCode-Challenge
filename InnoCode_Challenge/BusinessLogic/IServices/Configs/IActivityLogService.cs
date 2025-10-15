using Repository.DTOs.ActivityLogDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IActivityLogService
    {
        Task<PaginatedList<ActivityLogDTO>> GetAsync(ActivityLogQueryParams query);
        Task<ActivityLogDTO> GetByIdAsync(Guid id);
        Task<ActivityLogDTO> CreateAsync(CreateActivityLogDTO dto);
        Task DeleteAsync(Guid id);
    }
}
