using System.ComponentModel.DataAnnotations;
using Utility.Enums;

namespace Repository.DTOs.AppealDTOs
{
    public class ReviewAppealDTO
    {
        [Required(ErrorMessage = "Decision is required")]
        [RegularExpression("^(Approved|Rejected)$", ErrorMessage = "Decision must be either 'Approved' or 'Rejected'")]
        public string Decision { get; set; } = null!;

        [MaxLength(1000, ErrorMessage = "Decision reason cannot exceed 1000 characters")]
        public string? DecisionReason { get; set; }

        public AppealResolutionEnum? AppealResolution { get; set; }
    }
}
