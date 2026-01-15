using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class SchoolCreationRequest
{
    public Guid RequestId { get; set; }

    public Guid RequestedByUserId { get; set; }

    public string Name { get; set; } = null!;

    public string? Address { get; set; }

    public Guid ProvinceId { get; set; }

    public string? Contact { get; set; }

    public string Status { get; set; } = null!;

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? DenyReason { get; set; }

    public Guid? CreatedSchoolId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual School? CreatedSchool { get; set; }

    public virtual Province Province { get; set; } = null!;

    public virtual User RequestedByUser { get; set; } = null!;

    public virtual User? ReviewedByNavigation { get; set; }

    public virtual ICollection<SchoolCreationRequestEvidence> SchoolCreationRequestEvidences { get; set; } = new List<SchoolCreationRequestEvidence>();
}
