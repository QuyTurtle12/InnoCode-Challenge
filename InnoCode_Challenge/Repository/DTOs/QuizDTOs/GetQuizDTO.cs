using Repository.DTOs.McqOptionDTOs;

namespace Repository.DTOs.QuizDTOs
{
    public class GetQuizDTO
    {
        public Guid RoundId { get; set; }
        public string RoundName { get; set; } = string.Empty;
        public string RoundStatus { get; set; } = string.Empty;
        public McqTestDTO? McqTest { get; set; } = new();
    }

    public class McqTestDTO
    {
        public Guid TestId { get; set; }
        public List<QuestionDTO> Questions { get; set; } = new();

        public int TotalQuestions { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class QuestionDTO
    {
        public Guid QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public double Weight { get; set; } = 0;
        public int? OrderIndex { get; set; }
        public List<OptionDTO> Options { get; set; } = new();
    }

    public class OptionDTO
    {
        public Guid OptionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }
}
