using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.NotificationDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationsController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        /// <summary>
        /// Get All Notifications For Logged In User
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="recipientEmailSearch"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetAllAsync(int pageNumber = 1,
                                                    int pageSize = 10,
                                                    Guid? idSearch = null,
                                                    string? recipientEmailSearch = null)
        {
            PaginatedList<GetNotificationDTO>? notifications = await _notificationService.GetPaginatedNotificationAsync(pageNumber, pageSize, idSearch, recipientEmailSearch);
            return Ok(new BaseResponseModel<PaginatedList<GetNotificationDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: notifications,
                message: "Notifications retrieved successfully."
            ));
        }

        /// <summary>
        /// Get All Created Notifications 
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="recipientEmailSearch"></param>
        /// <returns></returns>
        [HttpGet("created-list")]
        public async Task<IActionResult> GetAllCreatedNotificationAsync(int pageNumber = 1,
                                                                        int pageSize = 10,
                                                                        Guid? idSearch = null,
                                                                        string? recipientEmailSearch = null)
        {
            PaginatedList<GetNotificationDTO>? notifications = await _notificationService.GetPaginatedCreatedNotificationsAsync(pageNumber, pageSize, idSearch, recipientEmailSearch);
            return Ok(new BaseResponseModel<PaginatedList<GetNotificationDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: notifications,
                message: "Notifications retrieved successfully."
            ));
        }

        /// <summary>
        /// Send Notification
        /// </summary>
        /// <param name="notificationDTO"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> SendGeneralNotificationAsync(CreateGeneralNotificationDTO notificationDTO)
        {
            await _notificationService.CreateNotification(notificationDTO);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                message: "Send notification successfully."
            ));
        }

    }
}
