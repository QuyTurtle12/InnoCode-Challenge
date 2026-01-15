
using System.Text.Json.Serialization;

namespace Api.IntegrationTests.Infrastructure
{
    public sealed class ErrorEnvelope
    {
        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        // chỉ có khi CoreException trả thêm additionalData
        [JsonPropertyName("additionalData")]
        public object? AdditionalData { get; set; }
    }
}