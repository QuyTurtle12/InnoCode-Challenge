namespace Repository.DTOs.CertificateDTOs
{
    public class GetCertificateDTO : BaseCertificateDTO
    {
        public Guid CertificateId { get; set; }

        public string CertificateName { get; set; } = string.Empty;

        public string TeamName { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        public DateTime IssuedAt { get; set; }
    }
}
