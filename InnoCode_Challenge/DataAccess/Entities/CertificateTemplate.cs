using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class CertificateTemplate
{
    public Guid TemplateId { get; set; }

    public Guid ContestId { get; set; }

    public string Name { get; set; } = null!;

    public string? FileUrl { get; set; }

    public virtual ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();

    public virtual Contest Contest { get; set; } = null!;
}
