using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class JudgeInvite
{
    public Guid InviteId { get; set; }

    public Guid JudgeId { get; set; }

    public Guid ContestId { get; set; }

    public string InviteCode { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public string? CreatedBy { get; set; }

    public virtual Contest Contest { get; set; } = null!;

    public virtual User Judge { get; set; } = null!;
}
