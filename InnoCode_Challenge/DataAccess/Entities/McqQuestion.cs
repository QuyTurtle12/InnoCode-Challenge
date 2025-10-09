using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class McqQuestion
{
    public Guid QuestionId { get; set; }

    public Guid? BankId { get; set; }

    public string Text { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Bank? Bank { get; set; }

    public virtual ICollection<McqAttemptItem> McqAttemptItems { get; set; } = new List<McqAttemptItem>();

    public virtual ICollection<McqOption> McqOptions { get; set; } = new List<McqOption>();

    public virtual ICollection<McqTestQuestion> McqTestQuestions { get; set; } = new List<McqTestQuestion>();
}
