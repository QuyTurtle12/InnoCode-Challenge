using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.RubricDTOs
{
    public class CreateRubricDTO
    {
        [Required]
        [MinLength(1, ErrorMessage = "At least one rubric criterion is required")]
        public List<CreateRubricCriterionDTO> Criteria { get; set; } = new List<CreateRubricCriterionDTO>();
    }

    public class CreateRubricCriterionDTO
    {
        [Required(ErrorMessage = "Description is required")]
        [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Range(0.1, double.MaxValue, ErrorMessage = "Max score must be greater than 0")]
        public double MaxScore { get; set; }
    }
}
