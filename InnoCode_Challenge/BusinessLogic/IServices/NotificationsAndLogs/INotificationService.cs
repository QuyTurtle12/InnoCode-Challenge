using Repository.DTOs.NotificationDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.NotificationsAndLogs
{
    public interface INotificationService
    {
        //User view
        Task<PaginatedList<GetNotificationDTO>> GetMyNotificationsAsync(int pageNumber, int pageSize, Guid? idSearch);
        // Staff/Admin view
        Task<PaginatedList<GetNotificationDTO>> GetCreatedNotificationsAsync(int pageNumber, int pageSize, Guid? idSearch, string? recipientEmailSearch);
        Task CreateNotificationAsync(CreateGeneralNotificationDTO dto);
        //Create in-app notification to multiple users
        Task CreateInAppToUsersAsync( IEnumerable<Guid> userIds, string type, object payload);
        //Create in-app notification to single user
        Task CreateInAppToUserAsync(Guid userId,string type,object payload);

    }
}
