namespace Repository.DTOs.McqAttemptDTOs
{
    public class BaseMcqAttemptDTO
    {
        public DateTime Start { get; set; }

        public DateTime? End { get; set; }

        public double? Score { get; set; }
    }
}
