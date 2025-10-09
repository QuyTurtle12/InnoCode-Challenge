using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class McqAttempt
{
    public Guid AttemptId { get; set; }

    public Guid TestId { get; set; }

    public Guid RoundId { get; set; }

    public Guid StudentId { get; set; }

    public DateTime Start { get; set; }

    public DateTime? End { get; set; }

    public double? Score { get; set; }

    public virtual ICollection<McqAttemptItem> McqAttemptItems { get; set; } = new List<McqAttemptItem>();

    public virtual Round Round { get; set; } = null!;

    public virtual Student Student { get; set; } = null!;

    public virtual McqTest Test { get; set; } = null!;
}
