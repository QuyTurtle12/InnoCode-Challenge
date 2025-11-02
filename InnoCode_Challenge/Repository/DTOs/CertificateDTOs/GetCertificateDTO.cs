namespace Repository.DTOs.CertificateDTOs
{
    public class GetCertificateDTO
    {
        public Guid TemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = string.Empty;
        public string FileUrl { get; set; } = null!;
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
    }

    public class GetAllTeamCertificateDTO : GetCertificateDTO
    {
        public List<TeamDetailDTO>? TeamDetails { get; set; } = null;
    }

    public class GetMyCertificateDTO : GetCertificateDTO
    {
        public Guid CertificateId { get; set; }
    }

    public class TeamDetailDTO
    {
        public Guid CertificateId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
    }
}
