namespace Repository.DTOs.CertificateTemplateDTOs
{
    public class BaseCertificateTemplateDTO
    {
        public Guid ContestId { get; set; }

        public string Name { get; set; } = null!;

        public string? FileUrl { get; set; }
    }
}
