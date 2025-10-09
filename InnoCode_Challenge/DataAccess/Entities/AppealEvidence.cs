using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class AppealEvidence
{
    public Guid EvidenceId { get; set; }

    public Guid AppealId { get; set; }

    public string Url { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Appeal Appeal { get; set; } = null!;
}
