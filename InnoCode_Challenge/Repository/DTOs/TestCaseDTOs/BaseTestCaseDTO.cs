namespace Repository.DTOs.TestCaseDTOs
{
    public class BaseTestCaseDTO
    {
        public Guid ProblemId { get; set; }

        public string? Description { get; set; }

        public double Weight { get; set; }

        public int? TimeLimitMs { get; set; }

        public int? MemoryKb { get; set; }
        public string? Input { get; set; }
        public string? ExpectedOutput { get; set; }
    }
}
