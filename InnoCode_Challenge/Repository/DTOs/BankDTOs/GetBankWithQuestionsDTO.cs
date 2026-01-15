using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.BankDTOs
{
    public class GetBankWithQuestionsDTO
    {
        public Guid BankId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalQuestions { get; set; }
        public List<BankQuestionDTO> Questions { get; set; } = new List<BankQuestionDTO>();
    }

    public class BankQuestionDTO
    {
        public Guid QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<BankOptionDTO> Options { get; set; } = new List<BankOptionDTO>();
    }

    public class BankOptionDTO
    {
        public Guid OptionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }
}
