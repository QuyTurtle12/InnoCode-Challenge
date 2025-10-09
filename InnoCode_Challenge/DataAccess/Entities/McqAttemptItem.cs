using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class McqAttemptItem
{
    public Guid ItemId { get; set; }

    public Guid AttemptId { get; set; }

    public Guid TestId { get; set; }

    public Guid QuestionId { get; set; }

    public Guid? SelectedOptionId { get; set; }

    public bool Correct { get; set; }

    public virtual McqAttempt Attempt { get; set; } = null!;

    public virtual McqQuestion Question { get; set; } = null!;

    public virtual McqOption? SelectedOption { get; set; }

    public virtual McqTest Test { get; set; } = null!;
}
