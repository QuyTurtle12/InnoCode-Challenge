using Repository.DTOs.McqOptionDTOs;

namespace Repository.DTOs.QuizDTOs
{
    public class GetQuizDTO
    {
        public Guid RoundId { get; set; }
        public List<McqTestDTO> McqTests { get; set; } = new();
    }

    public class McqTestDTO
    {
        public Guid TestId { get; set; }
        public List<QuestionDTO> Questions { get; set; } = new();
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
