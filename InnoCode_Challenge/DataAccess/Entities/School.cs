using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class School
{
    public Guid SchoolId { get; set; }

    public string Name { get; set; } = null!;

    public Guid ProvinceId { get; set; }

    public string? Contact { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<Mentor> Mentors { get; set; } = new List<Mentor>();

    public virtual Province Province { get; set; } = null!;

    public virtual ICollection<Student> Students { get; set; } = new List<Student>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}
