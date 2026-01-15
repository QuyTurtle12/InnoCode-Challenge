using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class SubmissionFingerprint
{
    public Guid FingerprintId { get; set; }

    public Guid SubmissionId { get; set; }

    public Guid ProblemId { get; set; }

    public Guid TeamId { get; set; }

    public string Algorithm { get; set; } = null!;

    public string Hash { get; set; } = null!;

    public int NormalizedLength { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Problem Problem { get; set; } = null!;

    public virtual Submission Submission { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
