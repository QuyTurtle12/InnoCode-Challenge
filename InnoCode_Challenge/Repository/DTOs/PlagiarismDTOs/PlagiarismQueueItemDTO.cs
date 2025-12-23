using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.PlagiarismDTOs
{
        public class PlagiarismQueueItemDTO
        {
            public Guid SubmissionId { get; set; }
            public Guid ProblemId { get; set; }
            public Guid RoundId { get; set; }
            public string RoundName { get; set; } = string.Empty;

            public Guid ContestId { get; set; }
            public string ContestName { get; set; } = string.Empty;

            public Guid TeamId { get; set; }
            public string TeamName { get; set; } = string.Empty;

            public Guid StudentId { get; set; }
            public string StudentName { get; set; } = string.Empty;

            public double Score { get; set; }
            public DateTime SubmittedAt { get; set; }

            public string Hash { get; set; } = string.Empty;
            public string Algorithm { get; set; } = string.Empty;
            public int NormalizedLength { get; set; }
        }
}
