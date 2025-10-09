using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Appeal
{
    public Guid AppealId { get; set; }

    public Guid TeamId { get; set; }

    public string TargetType { get; set; } = null!;

    public string TargetId { get; set; } = null!;

    public Guid OwnerId { get; set; }

    public string State { get; set; } = null!;

    public string? Reason { get; set; }

    public string? Decision { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<AppealEvidence> AppealEvidences { get; set; } = new List<AppealEvidence>();

    public virtual User Owner { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
