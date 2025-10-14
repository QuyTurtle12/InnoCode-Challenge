namespace Repository.DTOs.SubmissionDTOs
{
    public class GetSubmissionDTO : BaseSubmissionDTO
    {
        public Guid SubmissionId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
