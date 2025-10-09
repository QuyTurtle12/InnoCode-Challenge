using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class McqTestQuestion
{
    public Guid TestId { get; set; }

    public Guid QuestionId { get; set; }

    public double Weight { get; set; }

    public int? OrderIndex { get; set; }

    public virtual McqQuestion Question { get; set; } = null!;

    public virtual McqTest Test { get; set; } = null!;
}
