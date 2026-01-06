using Repository.DTOs.JudgeDTOs;

namespace Repository.DTOs.MockTestDTOs
{
    public class MockTestResultDTO
    {
        public Guid Id { get; set; }
        public string ProblemId { get; set; } = string.Empty;
        public MockTestSummaryDTO Summary { get; set; } = new();
        public string Language { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public List<MockTestCaseDetail> Details { get; set; } = new();
    }

    public class MockTestSummaryDTO
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public double rawScore { get; set; } = 0;
        public double penaltyScore { get; set; } = 0;
    }

    public class MockTestCaseDetail
    {
        public string TestName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
