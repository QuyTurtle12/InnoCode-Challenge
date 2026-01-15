namespace Repository.DTOs.McqAttemptItemDTOs
{
    public class BaseMcqAttemptItemDTO
    {
        public Guid AttemptId { get; set; }

        public Guid TestId { get; set; }

        public Guid QuestionId { get; set; }

        public Guid? SelectedOptionId { get; set; }

        public bool Correct { get; set; }
    }
}
