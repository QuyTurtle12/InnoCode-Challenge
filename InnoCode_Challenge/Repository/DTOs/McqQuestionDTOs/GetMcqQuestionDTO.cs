using DataAccess.Entities;

namespace Repository.DTOs.McqQuestionDTOs
{
    public class GetMcqQuestionDTO : BaseMcqQuestionDTO
    {
        public Guid QuestionId { get; set; }
        public Guid BankId { get; set; }
        public string BankName { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public ICollection<McqOption>? McqOptions { get; set; }
    }
}
