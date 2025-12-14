using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Province
{
    public Guid ProvinceId { get; set; }

    public string Name { get; set; } = null!;

    public string? Address { get; set; }

    public virtual ICollection<MentorRegistration> MentorRegistrations { get; set; } = new List<MentorRegistration>();

    public virtual ICollection<SchoolCreationRequest> SchoolCreationRequests { get; set; } = new List<SchoolCreationRequest>();

    public virtual ICollection<School> Schools { get; set; } = new List<School>();
}
