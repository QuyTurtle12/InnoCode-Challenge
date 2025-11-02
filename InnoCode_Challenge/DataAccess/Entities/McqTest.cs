using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class McqTest
{
    public Guid TestId { get; set; }

    public Guid RoundId { get; set; }

    public string? Name { get; set; }

    public string? Config { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<McqAttemptItem> McqAttemptItems { get; set; } = new List<McqAttemptItem>();

    public virtual ICollection<McqAttempt> McqAttempts { get; set; } = new List<McqAttempt>();

    public virtual ICollection<McqTestQuestion> McqTestQuestions { get; set; } = new List<McqTestQuestion>();

    public virtual Round Round { get; set; } = null!;
}
