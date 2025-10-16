namespace Repository.DTOs.JudgeDTOs
{
    public class JudgeSubmissionRequestDTO
    {
        public int LanguageId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? CodeUrl { get; set; }
        public JudgeProblemDTO Problem { get; set; } = new();
        public List<JudgeTestCaseDTO> TestCases { get; set; } = new();
        public double TimeLimitSec { get; set; } = 2.0;
        public int MemoryLimitKb { get; set; } = 128000;
    }

    public class JudgeProblemDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public class JudgeTestCaseDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Stdin { get; set; } = string.Empty;
        public string ExpectedOutput { get; set; } = string.Empty;
    }
}
