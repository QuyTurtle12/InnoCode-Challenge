using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Problem
{
    public Guid ProblemId { get; set; }

    public Guid RoundId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Language { get; set; } = null!;

    public string? Type { get; set; }

    public double? PenaltyRate { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? Description { get; set; }

    public virtual Round Round { get; set; } = null!;

    public virtual ICollection<Submission> Submissions { get; set; } = new List<Submission>();

    public virtual ICollection<TestCase> TestCases { get; set; } = new List<TestCase>();
}
