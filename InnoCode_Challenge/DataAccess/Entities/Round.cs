using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Round
{
    public Guid RoundId { get; set; }

    public Guid ContestId { get; set; }

    public string Name { get; set; } = null!;

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Contest Contest { get; set; } = null!;

    public virtual ICollection<McqAttempt> McqAttempts { get; set; } = new List<McqAttempt>();

    public virtual McqTest? McqTest { get; set; }

    public virtual Problem? Problem { get; set; }
}
