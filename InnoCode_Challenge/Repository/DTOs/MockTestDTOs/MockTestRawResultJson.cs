namespace Repository.DTOs.MockTestDTOs
{
    public class MockTestRawResultJson
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public List<MockTestDetailJson>? Details { get; set; }
    }

    public class MockTestDetailJson
    {
        public string? Test { get; set; }
        public string? Status { get; set; }
        public string? Expected { get; set; }
        public string? Actual { get; set; }
        public string? Stderr { get; set; }
    }
}
