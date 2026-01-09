namespace Repository.DTOs.DashboardDTOs
{
    public class TimeComparisonDTO
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Current vs Previous Period
        public MetricComparison<int> Contests { get; set; }
        public MetricComparison<int> Teams { get; set; }
        public MetricComparison<int> Submissions { get; set; }
    }

    public class MetricComparison<T>
    {
        public T CurrentValue { get; set; }
        public T PreviousValue { get; set; }
        public double ChangePercentage { get; set; }
        public string Trend { get; set; }
    }
}
