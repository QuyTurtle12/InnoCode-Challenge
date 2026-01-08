using System.Text.Json.Serialization;

namespace Repository.DTOs.MockTestDTOs
{
    public class MockTestRawResult
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("passed")]
        public int Passed { get; set; }

        [JsonPropertyName("failed")]
        public int Failed { get; set; }

        [JsonPropertyName("errors")]
        public int Errors { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("details")]
        public List<TestDetail>? Details { get; set; }

        public class TestDetail
        {
            [JsonPropertyName("test")]
            public string? Test { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }
    }
}