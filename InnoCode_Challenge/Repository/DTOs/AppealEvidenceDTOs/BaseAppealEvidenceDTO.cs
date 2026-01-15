namespace Repository.DTOs.AppealEvidenceDTOs
{
    public class BaseAppealEvidenceDTO
    {
        public Guid AppealId { get; set; }

        public string Url { get; set; } = null!;

        public string? Note { get; set; }
    }
}
