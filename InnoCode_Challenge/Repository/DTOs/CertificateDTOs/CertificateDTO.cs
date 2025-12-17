namespace Repository.DTOs.CertificateDTOs
{
    public class CertificateDTO
    {
        public Guid CertificateId { get; set; }
        public Guid TemplateId { get; set; }
        public string TemplateName { get; set; } = null!;
        public Guid ContestId { get; set; }
        public Guid? TeamId { get; set; }
        public string? TeamName { get; set; }
        public Guid? StudentId { get; set; }
        public string? StudentName { get; set; }
        public string FileUrl { get; set; } = null!;
        public DateTime IssuedAt { get; set; }
        public string CertificateType { get; set; } = null!;// Team | Student
    }
}
