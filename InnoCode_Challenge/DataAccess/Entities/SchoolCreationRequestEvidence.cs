using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class SchoolCreationRequestEvidence
{
    public Guid EvidenceId { get; set; }

    public Guid RequestId { get; set; }

    public string Url { get; set; } = null!;

    public string? Type { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual SchoolCreationRequest Request { get; set; } = null!;
}
