namespace Repository.DTOs.DashboardDTOs
{
    public class ChartDataDTO
    {
        // For line/bar charts
        public List<string> Labels { get; set; }
        public List<int> ContestCreationTrend { get; set; }
        public List<int> SubmissionTrend { get; set; }
        public List<int> TeamRegistrationTrend { get; set; }

        // For pie charts
        public Dictionary<string, int> SubmissionsByStatus { get; set; }
        public Dictionary<string, int> TeamsPerContest { get; set; }
    }
}
