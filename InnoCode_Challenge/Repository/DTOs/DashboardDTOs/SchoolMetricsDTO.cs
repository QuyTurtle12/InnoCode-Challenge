namespace Repository.DTOs.DashboardDTOs
{
    public class SchoolMetricsDTO
    {
        public int TotalSchools { get; set; }
        public List<TopSchoolDTO> TopSchoolsByParticipation { get; set; } = new();
        public Dictionary<string, int> TeamsByProvince { get; set; } = new();
    }

    public class TopSchoolDTO
    {
        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string ProvinceName { get; set; } = string.Empty;
        public int TotalTeams { get; set; }
        public int TotalStudents { get; set; }
        public int TotalCertificates { get; set; }
    }
}
