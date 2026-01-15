using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class SubmissionDetail
{
    public Guid DetailsId { get; set; }

    public Guid SubmissionId { get; set; }

    public Guid? TestcaseId { get; set; }

    public double? Weight { get; set; }

    public string? Note { get; set; }

    public int? RuntimeMs { get; set; }

    public int? MemoryKb { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Submission Submission { get; set; } = null!;

    public virtual TestCase? Testcase { get; set; }
}
