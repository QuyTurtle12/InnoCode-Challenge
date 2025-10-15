using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Student
{
    public Guid StudentId { get; set; }

    public Guid UserId { get; set; }

    public Guid SchoolId { get; set; }

    public string? Grade { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();

    public virtual ICollection<McqAttempt> McqAttempts { get; set; } = new List<McqAttempt>();

    public virtual School School { get; set; } = null!;

    public virtual ICollection<Submission> Submissions { get; set; } = new List<Submission>();

    public virtual ICollection<TeamInvite> TeamInvites { get; set; } = new List<TeamInvite>();

    public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();

    public virtual User User { get; set; } = null!;
}
