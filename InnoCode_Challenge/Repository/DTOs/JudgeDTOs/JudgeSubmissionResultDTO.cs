namespace Repository.DTOs.JudgeDTOs
{
    public class JudgeSubmissionResultDTO
    {
        public string ProblemId { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public JudgeSummaryDTO Summary { get; set; } = new();
        public List<JudgeCaseResultDTO> Cases { get; set; } = new();
    }

    public class JudgeSummaryDTO
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public double rawScore { get; set; } = 0;
        public double penaltyScore { get; set; } = 0;
    }

    public class JudgeCaseResultDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Judge0StatusId { get; set; } // 1 (In Queue), 2 (Processing), 3 (Accepted), 4+ (Various Error States)
        public string Judge0Status { get; set; } = string.Empty;
        public string Expected { get; set; } = string.Empty;
        public string Actual { get; set; } = string.Empty;
        public string? Stderr { get; set; }
        public string? CompileOutput { get; set; }
        public string? Time { get; set; }
        public int? MemoryKb { get; set; }
        public string Token { get; set; } = string.Empty;
    }

    public class JudgeSubmissionTokenDTO
    {
        public string Token { get; set; } = string.Empty;
    }

    public class JudgeResponseDTO
    {
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public string? CompileOutput { get; set; }
        public string? Message { get; set; }
        public JudgeStatusDTO? Status { get; set; }
        public string? Time { get; set; }
        public int? Memory { get; set; }
    }

    public class JudgeStatusDTO
    {
        public int Id { get; set; }
        public string? Description { get; set; }
    }
}
