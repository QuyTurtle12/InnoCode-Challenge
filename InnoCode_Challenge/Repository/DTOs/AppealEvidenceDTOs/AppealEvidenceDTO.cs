namespace Repository.DTOs.AppealEvidenceDTOs
{
    public class AppealEvidenceDTO
    {
        public Guid EvidenceId { get; set; }
        public string Url { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
