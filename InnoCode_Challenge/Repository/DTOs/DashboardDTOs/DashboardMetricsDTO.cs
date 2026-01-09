namespace Repository.DTOs.DashboardDTOs
{
    public class DashboardMetricsDTO
    {
        public int TotalContests { get; set; }
        public int TotalTeams { get; set; }
        public int TotalStudents { get; set; }
        public ContestStatusBreakdownDTO StatusBreakdown { get; set; } = new();

        // Trend indicators
        public double ContestGrowthRate { get; set; }
        public int NewContestsLastMonth { get; set; }
    }

    public class ContestStatusBreakdownDTO
    {
        // Individual status counts
        public int Published { get; set; }
        public int RegistrationOpen { get; set; }
        public int RegistrationClosed { get; set; }
        public int Ongoing { get; set; }
        public int Paused { get; set; }
        public int Completed { get; set; }
        public int Delayed { get; set; }
        public int Draft { get; set; }
        public int Cancelled { get; set; }

        // Total valid contests
        public int TotalValidContests =>
            Published + RegistrationOpen + RegistrationClosed +
            Ongoing + Paused + Completed + Delayed;
    }
}
