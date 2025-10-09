using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Team
{
    public Guid TeamId { get; set; }

    public Guid ContestId { get; set; }

    public Guid SchoolId { get; set; }

    public Guid MentorId { get; set; }

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<Appeal> Appeals { get; set; } = new List<Appeal>();

    public virtual ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();

    public virtual Contest Contest { get; set; } = null!;

    public virtual ICollection<LeaderboardEntry> LeaderboardEntries { get; set; } = new List<LeaderboardEntry>();

    public virtual Mentor Mentor { get; set; } = null!;

    public virtual School School { get; set; } = null!;

    public virtual ICollection<Submission> Submissions { get; set; } = new List<Submission>();

    public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();
}
