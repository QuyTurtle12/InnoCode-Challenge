using System;
using System.Collections.Generic;

namespace DataAccess.Entities;

public partial class Submission
{
    public Guid SubmissionId { get; set; }

    public Guid TeamId { get; set; }

    public Guid ProblemId { get; set; }

    public Guid SubmittedByStudentId { get; set; }

    public string? JudgedBy { get; set; }

    public string? Status { get; set; }

    public double Score { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Problem Problem { get; set; } = null!;

    public virtual ICollection<SubmissionArtifact> SubmissionArtifacts { get; set; } = new List<SubmissionArtifact>();

    public virtual ICollection<SubmissionDetail> SubmissionDetails { get; set; } = new List<SubmissionDetail>();

    public virtual Student? SubmittedByStudent { get; set; }

    public virtual Team Team { get; set; } = null!;
}
