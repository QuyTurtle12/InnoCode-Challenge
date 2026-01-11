namespace Repository.DTOs.DashboardDTOs
{
    public class OrganizerDashboardDTO
    {
        public int TotalContestsCreated { get; set; }

        public int ActiveContests { get; set; }

        public int CompletedContests { get; set; }

        public int DraftContests { get; set; }

        public ContestSummaryDTO? MostActiveContest { get; set; }

        public ContestSummaryDTO? ContestWithMostAppeals { get; set; }
    }

    public class ContestSummaryDTO
    {
        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Year { get; set; }

        public int TotalTeams { get; set; }

        public int TotalStudents { get; set; }

        public int TotalAppeals { get; set; }

        public TeamStatusBreakdownDTO TeamStatusBreakdown { get; set; } = new();

        public int CertificatesIssued { get; set; }

        public double ProgressPercentage { get; set; }

        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }
        public DateTime? ContestStart { get; set; }
        public DateTime? ContestEnd { get; set; }
    }
}
