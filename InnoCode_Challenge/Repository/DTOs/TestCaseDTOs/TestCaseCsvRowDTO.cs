using CsvHelper.Configuration.Attributes;

namespace Repository.DTOs.TestCaseDTOs
{
    public class TestCaseCsvRowDTO
    {
        [Name("Description")]
        public string? Description { get; set; }

        [Name("Weight")]
        public string Weight { get; set; } = string.Empty;

        [Name("TimeLimitMs")]
        public string? TimeLimitMs { get; set; }

        [Name("MemoryKb")]
        public string? MemoryKb { get; set; }

        [Name("Input")]
        public string? Input { get; set; }

        [Name("ExpectedOutput")]
        public string ExpectedOutput { get; set; } = string.Empty;
    }

    public class TestCaseImportResultDTO
    {
        public Guid ProblemId { get; set; }
        public Guid RoundId { get; set; }
        public string RoundName { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<Guid> ImportedTestCaseIds { get; set; } = new();
    }
}
