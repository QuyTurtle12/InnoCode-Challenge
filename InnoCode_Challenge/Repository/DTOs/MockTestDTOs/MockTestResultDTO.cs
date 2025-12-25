namespace Repository.DTOs.MockTestDTOs
{
    public class MockTestResultDTO
    {
        public bool Success { get; set; }
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public string? ErrorMessage { get; set; }
        public List<MockTestCaseDetail> Details { get; set; } = new();
    }

    public class MockTestCaseDetail
    {
        public string TestName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
