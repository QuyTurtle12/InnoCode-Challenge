namespace Repository.DTOs.McqOptionDTOs
{
    public class GetMcqOptionDTO : BaseMcqOptionDTO
    {
        public Guid OptionId { get; set; }

        public Guid QuestionId { get; set; }
    }
}
