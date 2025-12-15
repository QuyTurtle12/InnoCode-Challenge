using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Notification
{
    public Guid NotificationId { get; set; }

    public Guid UserId { get; set; }

    public string Type { get; set; } = null!;

    public string Channel { get; set; } = null!;

    public string? Payload { get; set; }

    public DateTime SentAt { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public virtual User User { get; set; } = null!;
}
