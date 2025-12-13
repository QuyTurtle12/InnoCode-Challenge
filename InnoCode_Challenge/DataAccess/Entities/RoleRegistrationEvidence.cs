using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class RoleRegistrationEvidence
{
    public Guid EvidenceId { get; set; }

    public Guid RegistrationId { get; set; }

    public string Url { get; set; } = null!;

    public string? Type { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual RoleRegistration Registration { get; set; } = null!;
}
