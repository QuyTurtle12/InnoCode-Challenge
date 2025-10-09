using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class SubmissionArtifact
{
    public Guid ArtifactId { get; set; }

    public Guid SubmissionId { get; set; }

    public string Type { get; set; } = null!;

    public string Url { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Submission Submission { get; set; } = null!;
}
