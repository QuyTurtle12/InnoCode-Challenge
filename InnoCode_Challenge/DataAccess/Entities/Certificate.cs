using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Certificate
{
    public Guid CertificateId { get; set; }

    public Guid TemplateId { get; set; }

    public Guid? TeamId { get; set; }

    public Guid? StudentId { get; set; }

    public string FileUrl { get; set; } = null!;

    public DateTime IssuedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Student? Student { get; set; }

    public virtual Team? Team { get; set; }

    public virtual CertificateTemplate Template { get; set; } = null!;
}
