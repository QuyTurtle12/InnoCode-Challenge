namespace Repository.DTOs.CertificateDTOs
{
    public class AwardCertificateDTO
    {
        public Guid templateId { get; set; }
        public List<Guid> teamIdList { get; set; } = new();
    }
}
