namespace Repository.DTOs.SubmissionDTOs
{
    public class SubmissionDistributionDTO
    {
        public Guid SubmissionId { get; set; }
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public Guid SubmittedByStudentId { get; set; }
        public string SubmitedByStudentName { get; set; } = string.Empty;
        public Guid? JudgeUserId { get; set; }
        public string? JudgeEmail { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
