using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class McqOption
{
    public Guid OptionId { get; set; }

    public Guid QuestionId { get; set; }

    public string Text { get; set; } = null!;

    public bool IsCorrect { get; set; }

    public virtual ICollection<McqAttemptItem> McqAttemptItems { get; set; } = new List<McqAttemptItem>();

    public virtual McqQuestion Question { get; set; } = null!;
}
