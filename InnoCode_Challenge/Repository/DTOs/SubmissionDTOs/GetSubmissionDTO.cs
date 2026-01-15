using Repository.DTOs.SubmissionArtifactDTOs;
using Repository.DTOs.SubmissionDetailDTOs;

namespace Repository.DTOs.SubmissionDTOs
{
    public class GetSubmissionDTO : BaseSubmissionDTO
    {
        public Guid SubmissionId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        public Guid? SubmittedByStudentId { get; set; }

        public string SubmittedByStudentName { get; set; } = string.Empty;

        public string? Status { get; set; }

        public double Score { get; set; }

        public int submissionAttemptNumber { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<GetSubmissionDetailDTO>? Details { get; set; } = null;

        public List<GetSubmissionArtifactDTO>? Artifacts { get; set; } = null;
    }
}
