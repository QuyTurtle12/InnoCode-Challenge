using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.NotificationDTOs
{
    public class CreateGeneralNotificationDTO : BaseNotificationDTO
    {
        [Required]
        public string Message { get; set; } = string.Empty;
    }
}
