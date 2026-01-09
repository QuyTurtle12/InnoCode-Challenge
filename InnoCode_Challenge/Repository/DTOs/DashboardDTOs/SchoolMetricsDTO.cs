namespace Repository.DTOs.DashboardDTOs
{
    public class SchoolMetricsDTO
    {
        public int TotalSchools { get; set; }
        public List<TopSchoolDTO> TopSchoolsByParticipation { get; set; }
        public Dictionary<string, int> TeamsByProvince { get; set; }
    }

    public class TopSchoolDTO
    {
        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; }
        public string Province { get; set; }
        public int TotalTeams { get; set; }
        public int TotalCertificates { get; set; }
    }
}
