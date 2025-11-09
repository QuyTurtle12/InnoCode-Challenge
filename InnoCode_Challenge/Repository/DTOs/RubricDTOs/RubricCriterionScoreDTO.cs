using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.RubricDTOs
{
    public class RubricCriterionScoreDTO
    {
        [Required]
        public Guid RubricId { get; set; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Score must be non-negative")]
        public double Score { get; set; }

        public string? Note { get; set; }
    }

    public class SubmitRubricScoreDTO
    {
        [Required]
        [MinLength(1, ErrorMessage = "At least one criterion score is required")]
        public List<RubricCriterionScoreDTO> CriterionScores { get; set; } = new List<RubricCriterionScoreDTO>();
    }

    public class RubricTemplateDTO
    {
        public Guid ProblemId { get; set; }
        public string? ProblemDescription { get; set; }
        public List<RubricCriterionDTO> Criteria { get; set; } = new List<RubricCriterionDTO>();
        public double TotalMaxScore { get; set; }
    }

    public class RubricCriterionDTO
    {
        public Guid RubricId { get; set; }
        public string? Description { get; set; }
        public double MaxScore { get; set; }
        public int Order { get; set; }
    }

    public class RubricEvaluationResultDTO
    {
        public Guid SubmissionId { get; set; }
        public string? StudentName { get; set; }
        public string? TeamName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? JudgedBy { get; set; }
        public double TotalScore { get; set; }
        public double MaxPossibleScore { get; set; }
        public List<RubricCriterionResultDTO> CriterionResults { get; set; } = new List<RubricCriterionResultDTO>();
    }

    public class RubricCriterionResultDTO
    {
        public Guid RubricId { get; set; }
        public string? Description { get; set; }
        public double MaxScore { get; set; }
        public double Score { get; set; }
        public string? Note { get; set; }
    }
}
