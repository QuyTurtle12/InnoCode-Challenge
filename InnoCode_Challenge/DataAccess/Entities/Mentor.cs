using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Mentor
{
    public Guid MentorId { get; set; }

    public Guid UserId { get; set; }

    public Guid SchoolId { get; set; }

    public string? Phone { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual School School { get; set; } = null!;

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual User User { get; set; } = null!;
}
