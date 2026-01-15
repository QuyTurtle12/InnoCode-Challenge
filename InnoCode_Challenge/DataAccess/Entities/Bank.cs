using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Bank
{
    public Guid BankId { get; set; }

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? CreatedBy { get; set; }

    public virtual ICollection<McqQuestion> McqQuestions { get; set; } = new List<McqQuestion>();
}
