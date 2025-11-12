using CsvHelper.Configuration.Attributes;

namespace Repository.DTOs.QuizDTOs
{
    public class McqCsvRowDTO
    {
        [Name("QuestionText")]
        public string QuestionText { get; set; } = string.Empty;

        [Name("OptionA")]
        public string OptionA { get; set; } = string.Empty;

        [Name("OptionB")]
        public string OptionB { get; set; } = string.Empty;

        [Name("OptionC")]
        public string? OptionC { get; set; }

        [Name("OptionD")]
        public string? OptionD { get; set; }

        [Name("CorrectAnswer")]
        public string CorrectAnswer { get; set; } = string.Empty;
    }

    public class McqImportResultDTO
    {
        public string BankName { get; set; } = string.Empty;
        public Guid BankId { get; set; }
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<Guid> ImportedQuestionIds { get; set; } = new();
    }
}
