using System;

namespace Repository.DTOs.ActivityLogDTOs
{
    public class ActivityLogDTO
    {
        public Guid LogId { get; set; }
        public Guid UserId { get; set; }
        public string Action { get; set; } = null!;
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public DateTime At { get; set; }
    }
}
