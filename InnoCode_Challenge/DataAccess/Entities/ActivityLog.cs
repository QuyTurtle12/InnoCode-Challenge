namespace DataAccess.Entities;

public partial class ActivityLog
{
    public Guid LogId { get; set; }

    public Guid UserId { get; set; }

    public string Action { get; set; } = null!;

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    public DateTime At { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
