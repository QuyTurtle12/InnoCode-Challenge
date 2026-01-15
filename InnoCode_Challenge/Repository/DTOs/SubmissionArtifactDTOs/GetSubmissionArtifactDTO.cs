namespace Repository.DTOs.SubmissionArtifactDTOs
{
    public class GetSubmissionArtifactDTO : BaseSubmissionArtifactDTO
    {
        public Guid ArtifactId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
