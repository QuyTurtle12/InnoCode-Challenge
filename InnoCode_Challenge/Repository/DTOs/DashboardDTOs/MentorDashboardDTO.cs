namespace Repository.DTOs.DashboardDTOs
{
    public class MentorDashboardDTO
    {
        public int TotalTeamsManaged { get; set; }

        public int ContestsParticipated { get; set; }

        public int TotalTeamCertificates { get; set; }

        public int TotalStudentsMentored { get; set; }

        public TeamStatusBreakdownDTO TeamStatusBreakdown { get; set; } = new();

        public ContestActivityDTO ContestActivity { get; set; } = new();

        public List<RecentCertificateDTO> RecentCertificates { get; set; } = new();

        public string SchoolName { get; set; } = string.Empty;
    }

    public class BestTeamDTO
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public int TotalCertificates { get; set; }
        public int TeamCertificates { get; set; }
        public int StudentCertificates { get; set; }
        public string ContestName { get; set; } = string.Empty;
    }

    public class TeamStatusBreakdownDTO
    {
        public int ActiveTeams { get; set; }
        public int CompletedTeams { get; set; }
        public int EliminatedTeams { get; set; }
        public int DisqualifiedTeams { get; set; }
    }

    public class ContestActivityDTO
    {
        public int OngoingContests { get; set; }
        public int CompletedContests { get; set; }
        public int UpcomingContests { get; set; }
    }

    public class RecentCertificateDTO
    {
        public Guid CertificateId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string CertificateType { get; set; } = string.Empty;
        public string ContestName { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
    }
}
