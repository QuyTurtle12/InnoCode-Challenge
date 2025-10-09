using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class TestCase
{
    public Guid TestCaseId { get; set; }

    public Guid ProblemId { get; set; }

    public string? Description { get; set; }

    public string Type { get; set; } = null!;

    public double Weight { get; set; }

    public int? TimeLimitMs { get; set; }

    public int? MemoryKb { get; set; }

    public virtual Problem Problem { get; set; } = null!;

    public virtual ICollection<SubmissionDetail> SubmissionDetails { get; set; } = new List<SubmissionDetail>();
}
