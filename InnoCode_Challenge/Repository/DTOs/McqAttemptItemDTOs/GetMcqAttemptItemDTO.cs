namespace Repository.DTOs.McqAttemptItemDTOs
{
    public class GetMcqAttemptItemDTO : BaseMcqAttemptItemDTO
    {
        public Guid ItemId { get; set; }
        public string TestName { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string OptionText { get; set; } = string.Empty;
    }
}
