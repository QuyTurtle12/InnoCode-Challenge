using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.McqTestQuestionDTOs
{
    public class BulkUpdateQuestionWeightsDTO
    {
        [Required]
        [MinLength(1, ErrorMessage = "At least one question weight must be provided.")]
        public List<UpdateQuestionWeightDTO> Questions { get; set; } = new();
    }

    public class UpdateQuestionWeightDTO
    {
        [Required]
        public Guid QuestionId { get; set; }

        [Required]
        [Range(0.1, double.MaxValue, ErrorMessage = "Weight must be greater than zero.")]
        public double Weight { get; set; }
    }
}
