using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class TeamMember
{
    public Guid TeamId { get; set; }

    public Guid StudentId { get; set; }

    public string? MemberRole { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual Student Student { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
