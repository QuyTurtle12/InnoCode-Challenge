using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Attachment
{
    public Guid AttachmentId { get; set; }

    public string Url { get; set; } = null!;

    public string? Type { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
