﻿namespace Repository.DTOs.QuizDTOs
{
    public class CreateQuizSubmissionDTO
    {
        public Guid TestId { get; set; }
        public List<QuizAnswerDTO> Answers { get; set; } = new List<QuizAnswerDTO>();
    }

    public class QuizAnswerDTO
    {
        public Guid QuestionId { get; set; }
        public Guid SelectedOptionId { get; set; }
    }
}
