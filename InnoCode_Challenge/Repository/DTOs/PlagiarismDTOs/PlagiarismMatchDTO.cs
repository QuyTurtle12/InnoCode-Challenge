using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.DTOs.SubmissionDetailDTOs;
using System;
using System.Collections.Generic;

namespace Repository.DTOs.PlagiarismDTOs
{
    public class PlagiarismMatchDTO
    {
        public Guid SubmissionId { get; set; }
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;

        public DateTime SubmittedAt { get; set; }
        public double Score { get; set; }

        public List<GetSubmissionArtifactDTO> Artifacts { get; set; } = new();
        public List<GetSubmissionDetailDTO> Details { get; set; } = new();
    }
}
