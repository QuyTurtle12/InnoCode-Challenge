namespace Repository.DTOs.SubmissionDetailDTOs
{
    public class BaseSubmissionDetailDTO
    {
        public Guid SubmissionId { get; set; }

        public Guid TestcaseId { get; set; }

        public int? Weight { get; set; }

        public string? Note { get; set; }

        public int? RuntimeMs { get; set; }

        public int? MemoryKb { get; set; }
    }
}
