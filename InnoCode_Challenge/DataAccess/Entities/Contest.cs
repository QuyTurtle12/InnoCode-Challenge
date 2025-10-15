using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Contest
{
    public Guid ContestId { get; set; }

    public int Year { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string ImgUrl { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public virtual ICollection<CertificateTemplate> CertificateTemplates { get; set; } = new List<CertificateTemplate>();

    public virtual ICollection<LeaderboardEntry> LeaderboardEntries { get; set; } = new List<LeaderboardEntry>();

    public virtual ICollection<Round> Rounds { get; set; } = new List<Round>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}
