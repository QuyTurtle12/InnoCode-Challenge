using Microsoft.AspNetCore.Http;
using Repository.DTOs.AppealEvidenceDTOs;
using System.ComponentModel.DataAnnotations;
using Utility.Enums;

namespace Repository.DTOs.AppealDTOs
{
    public class CreateAppealDTO
    {
        [Required(ErrorMessage = "Round ID is required")]
        public Guid RoundId { get; set; }

        [Required(ErrorMessage = "Team ID is required")]
        public Guid TeamId { get; set; }

        [Required(ErrorMessage = "Student ID is required")]
        public Guid StudentId { get; set; }

        [Required(ErrorMessage = "Reason is required")]
        [MaxLength(1000, ErrorMessage = "Reason cannot exceed 1000 characters")]
        public string Reason { get; set; } = null!;

        public AppealResolutionEnum AppealResolution { get; set; }

        public List<AppealEvidenceFileDTO>? Evidences { get; set; }
    }
}
