namespace Repository.DTOs.SubmissionArtifactDTOs
{
    public class BaseSubmissionArtifactDTO
    {
        public Guid SubmissionId { get; set; }

        public string Type { get; set; } = null!;

        public string Url { get; set; } = null!;
    }
}
