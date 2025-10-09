using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class LeaderboardEntry
{
    public Guid EntryId { get; set; }

    public Guid ContestId { get; set; }

    public Guid TeamId { get; set; }

    public int? Rank { get; set; }

    public double? Score { get; set; }

    public DateTime SnapshotAt { get; set; }

    public virtual Contest Contest { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
