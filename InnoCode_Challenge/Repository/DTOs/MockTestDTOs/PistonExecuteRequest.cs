using System.Text.Json.Serialization;

namespace Repository.DTOs.MockTestDTOs
{
    public class PistonExecuteRequest
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = "python";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "3.10.0";

        [JsonPropertyName("files")]
        public PistonFile[] Files { get; set; } = Array.Empty<PistonFile>();

        [JsonPropertyName("compile_timeout")]
        public int CompileTimeout { get; set; } = 10000;

        [JsonPropertyName("run_timeout")]
        public int RunTimeout { get; set; } = 30000;

        [JsonPropertyName("compile_memory_limit")]
        public long CompileMemoryLimit { get; set; }

        [JsonPropertyName("run_memory_limit")]
        public long RunMemoryLimit { get; set; }
    }

    public class PistonFile
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
