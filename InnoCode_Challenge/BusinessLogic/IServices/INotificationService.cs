using Repository.DTOs.NotificationDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface INotificationService
    {
        Task<PaginatedList<GetNotificationDTO>> GetPaginatedNotificationAsync(int pageNumber, int pageSize, Guid? idSearch, string? recipientEmailSearch);
        Task<PaginatedList<GetNotificationDTO>> GetPaginatedCreatedNotificationsAsync(int pageNumber, int pageSize, Guid? idSearch, string? recipientEmailSearch);
        Task CreateNotification(BaseNotificationDTO notificationDTO);
    }
}
