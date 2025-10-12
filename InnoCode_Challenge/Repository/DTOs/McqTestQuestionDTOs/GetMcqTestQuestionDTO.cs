namespace Repository.DTOs.McqTestQuestionDTOs
{
    public class GetMcqTestQuestionDTO : BaseMcqTestQuestionDTO
    {
        public Guid TestId { get; set; }

        public string TestName { get; set; } = string.Empty;

        public Guid QuestionId { get; set; }

        public string QuestionText { get; set; } = string.Empty;
    }
}
