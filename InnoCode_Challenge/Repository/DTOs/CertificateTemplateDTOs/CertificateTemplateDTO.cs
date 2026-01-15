namespace Repository.DTOs.CertificateTemplateDTOs
{
    public class CertificateTemplateDTO
    {
        public Guid TemplateId { get; set; }
        public Guid ContestId { get; set; }
        public string Name { get; set; } = null!;
        public string? FileUrl { get; set; }
        public TextLayoutDTO Text { get; set; } = new();
    }
}
