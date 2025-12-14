using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
