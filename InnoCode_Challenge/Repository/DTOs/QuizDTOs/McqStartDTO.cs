namespace Repository.DTOs.QuizDTOs
{
    public class McqStartDTO
    {
        public string key { get; set; } = string.Empty;
        public DateTime startTime { get; set; }
        public int TimeLimitInSeconds { get; set; }
    }
}
