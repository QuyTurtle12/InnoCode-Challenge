namespace Repository.DTOs.QuizDTOs
{
    public class QuizResultDTO
    {
        public Guid AttemptId { get; set; }
        public Guid TestId { get; set; }
        public string TestName { get; set; } = string.Empty;
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectAnswers { get; set; }
        public double Score { get; set; }
        public List<QuizAnswerResultDTO> AnswerResults { get; set; } = new List<QuizAnswerResultDTO>();
    }

    public class QuizAnswerResultDTO
    {
        public Guid QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public Guid SelectedOptionId { get; set; }
        public string SelectedOptionText { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class QuizAttemptSummaryDTO
    {
        public Guid AttemptId { get; set; }
        public Guid TestId { get; set; }
        public string TestName { get; set; } = string.Empty;
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double Score { get; set; }
    }
}
