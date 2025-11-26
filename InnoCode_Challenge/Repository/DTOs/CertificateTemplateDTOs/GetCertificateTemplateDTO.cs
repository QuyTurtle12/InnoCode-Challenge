namespace Repository.DTOs.CertificateTemplateDTOs
{
    public class GetCertificateTemplateDTO : CertificateTemplateDTO
    {
        public Guid TemplateId { get; set; }

        public string ContestName { get; set; } = string.Empty;

        public string? FileUrl { get; set; }
    }
}
