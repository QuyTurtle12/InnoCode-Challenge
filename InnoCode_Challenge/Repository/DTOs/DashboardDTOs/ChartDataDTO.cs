namespace Repository.DTOs.DashboardDTOs
{
    public class ChartDataDTO
    {
        public List<string> Labels { get; set; } = new();
        public List<int> ContestCreationTrend { get; set; } = new();
        public List<int> TeamRegistrationTrend { get; set; } = new();
        public Dictionary<string, int> ContestsByStatus { get; set; } = new();
    }

    public class TrendDataPoint
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Count { get; set; }
        public int ContestCount { get; set; }
        public int TeamCount { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
