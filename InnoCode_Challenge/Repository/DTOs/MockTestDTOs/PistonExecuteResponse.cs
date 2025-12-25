using System.Text.Json.Serialization;

namespace Repository.DTOs.MockTestDTOs
{
    public class PistonExecuteResponse
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("run")]
        public PistonRunResult? Run { get; set; }
    }

    public class PistonRunResult
    {
        [JsonPropertyName("stdout")]
        public string? Output { get; set; }

        [JsonPropertyName("stderr")]
        public string? Stderr { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("signal")]
        public string? Signal { get; set; }
    }

}
