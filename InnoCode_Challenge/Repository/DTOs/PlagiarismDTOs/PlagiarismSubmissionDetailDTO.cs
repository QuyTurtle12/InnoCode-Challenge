using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.DTOs.SubmissionDetailDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.PlagiarismDTOs
{
    public class PlagiarismSubmissionDetailDTO
    {
        public PlagiarismQueueItemDTO Submission { get; set; } = new();

        public List<GetSubmissionArtifactDTO> Artifacts { get; set; } = new();
        public List<GetSubmissionDetailDTO> Details { get; set; } = new();

        public List<PlagiarismMatchDTO> Matches { get; set; } = new();
    }

}
