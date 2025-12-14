using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.PlagiarismDTOs
{
    public enum PlagiarismResolutionEnum
    {
        Cleared = 1,     // not plagiarism
        Confirmed = 2    // plagiarism
    }

    public class ResolvePlagiarismDTO
    {
        public PlagiarismResolutionEnum Resolution { get; set; }
        public bool ApplyLeaderboard { get; set; } = true;
        public string? Note { get; set; }
    }
}
