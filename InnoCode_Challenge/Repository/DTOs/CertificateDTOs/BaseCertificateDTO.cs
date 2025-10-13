namespace Repository.DTOs.CertificateDTOs
{
     public class BaseCertificateDTO
    {
        public Guid TemplateId { get; set; }

        public Guid? TeamId { get; set; }

        public Guid? StudentId { get; set; }

        public string FileUrl { get; set; } = null!;
    }
}
