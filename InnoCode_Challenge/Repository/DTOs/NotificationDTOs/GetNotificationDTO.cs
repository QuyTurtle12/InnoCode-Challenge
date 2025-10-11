namespace Repository.DTOs.NotificationDTOs
{
    public class GetNotificationDTO
    {
        public Guid NotificationId { get; set; }
        public IList<string> recipientEmailList { get; set; } = new List<string>();
        public string Type { get; set; } = null!;
        public string Channel { get; set; } = null!;
        public string? Payload { get; set; }
        public DateTime SentAt { get; set; }
    }
}
