using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class RoleRegistration
{
    public Guid RegistrationId { get; set; }

    public string RequestedRole { get; set; } = null!;

    public string Fullname { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public string? Phone { get; set; }

    public string? Payload { get; set; }

    public string Status { get; set; } = null!;

    public string? DenyReason { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual User? ReviewedByNavigation { get; set; }

    public virtual ICollection<RoleRegistrationEvidence> RoleRegistrationEvidences { get; set; } = new List<RoleRegistrationEvidence>();
}
