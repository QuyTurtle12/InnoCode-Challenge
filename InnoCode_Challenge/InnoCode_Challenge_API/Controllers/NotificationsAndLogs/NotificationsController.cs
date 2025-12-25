using BusinessLogic.IServices.NotificationsAndLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.NotificationDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.NotificationsAndLogs
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

        [HttpGet]
        public async Task<IActionResult> GetAllAsync(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null)
        {
            PaginatedList<GetNotificationDTO> notifications =
                await _notificationService.GetMyNotificationsAsync(pageNumber, pageSize, idSearch);

            return Ok(new BaseResponseModel<PaginatedList<GetNotificationDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: notifications,
                message: "Notifications retrieved successfully."
            ));
        }


        [HttpGet("created-list")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> GetAllCreatedNotificationAsync(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            string? recipientEmailSearch = null)
        {
            PaginatedList<GetNotificationDTO> notifications =
                await _notificationService.GetCreatedNotificationsAsync(pageNumber, pageSize, idSearch, recipientEmailSearch);

            return Ok(new BaseResponseModel<PaginatedList<GetNotificationDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: notifications,
                message: "Notifications retrieved successfully."
            ));
        }


        [HttpPost]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> SendGeneralNotificationAsync([FromBody] CreateGeneralNotificationDTO dto)
        {
            await _notificationService.CreateNotificationAsync(dto);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                message: "Send notification successfully."
            ));
        }

        [Authorize]
        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var result = await _notificationService.MarkAsReadAsync(id);

            return Ok(new BaseResponseModel<MarkReadResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Notification marked as read."
            ));
        }

        [Authorize]
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var result = await _notificationService.MarkAllAsReadAsync(DateTime.UtcNow);

            return Ok(new BaseResponseModel<MarkReadResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "All notifications marked as read."
            ));
        }

        [Authorize]
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var result = await _notificationService.GetUnreadCountAsync();

            return Ok(new BaseResponseModel<UnreadCountDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "OK"
            ));
        }


    }
}
