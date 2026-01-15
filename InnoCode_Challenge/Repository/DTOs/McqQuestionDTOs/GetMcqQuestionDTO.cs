using DataAccess.Entities;
using Repository.DTOs.McqOptionDTOs;

namespace Repository.DTOs.McqQuestionDTOs
{
    public class GetMcqQuestionDTO : BaseMcqQuestionDTO
    {
        public Guid QuestionId { get; set; }
        public Guid BankId { get; set; }
        public string BankName { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public ICollection<GetMcqOptionDTO>? McqOptions { get; set; }
    }
}
