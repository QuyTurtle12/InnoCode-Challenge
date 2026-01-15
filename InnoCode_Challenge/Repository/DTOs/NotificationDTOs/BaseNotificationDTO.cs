using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.NotificationDTOs
{
    public class BaseNotificationDTO
    {
        [Required]
        public IList<string> recipientEmailList { get; set; } = new List<string>();

        [Required]
        [EnumDataType(typeof(NotificationTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public NotificationTypeEnum Type { get; set; }

        [Required]
        [EnumDataType(typeof(NotificationChannelEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public NotificationChannelEnum Channel { get; set; }
    }
}
