using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Config
{
    public string Key { get; set; } = null!;

    public string? Value { get; set; }

    public string? Scope { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
