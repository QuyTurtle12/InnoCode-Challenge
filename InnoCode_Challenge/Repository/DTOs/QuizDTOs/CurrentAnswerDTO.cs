namespace Repository.DTOs.QuizDTOs
{
    public class CurrentAnswerDTO
    {
        public Guid QuestionId { get; set; }
        public Guid SelectedOptionId { get; set; }
    }

    public class SaveAnswerDTO
    {
        public string Key { get; set; } = string.Empty;
        public int TimeLimitSeconds { get; set; } = 0;
        public List<CurrentAnswerDTO> Answers { get; set; } = new List<CurrentAnswerDTO>();
    }
}
