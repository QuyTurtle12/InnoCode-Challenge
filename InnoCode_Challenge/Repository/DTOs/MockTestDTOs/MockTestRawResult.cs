namespace Repository.DTOs.MockTestDTOs
{
    public class MockTestRawResult
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Errors { get; set; }
        public bool Success { get; set; }
        public List<TestDetail>? Details { get; set; }

        public class TestDetail
        {
            public string? Test { get; set; }
            public string? Status { get; set; }
            public string? Message { get; set; }
        }
    }
}
