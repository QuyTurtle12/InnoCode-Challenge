using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class User
{
    public Guid UserId { get; set; }

    public string Email { get; set; } = null!;

    public string Role { get; set; } = null!;

    public string Fullname { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();

    public virtual ICollection<Appeal> Appeals { get; set; } = new List<Appeal>();

    public virtual ICollection<MentorRegistration> MentorRegistrations { get; set; } = new List<MentorRegistration>();

    public virtual ICollection<Mentor> Mentors { get; set; } = new List<Mentor>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<Student> Students { get; set; } = new List<Student>();

    public virtual ICollection<TeamInvite> TeamInvites { get; set; } = new List<TeamInvite>();
}
