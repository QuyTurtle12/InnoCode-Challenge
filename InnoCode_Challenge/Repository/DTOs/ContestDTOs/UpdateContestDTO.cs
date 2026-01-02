using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ContestDTOs
{
    public class UpdateContestDTO : BaseContestDTO
    {
        // Registration timeline
        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }

        // Team configuration
        [Range(1, 50)]
        public int? TeamMembersMax { get; set; }
        [Range(1, 50)]
        public int? TeamMembersMin { get; set; }

        [Range(1, 10000)]
        public int? TeamLimitMax { get; set; }

        // Appeal / judge policy (days)
        [Range(0, 365)]
        public int? AppealSubmitDays { get; set; }

        [Range(0, 365)]
        public int? AppealReviewDays { get; set; }

        [Range(0, 365)]
        public int? JudgeRescoreDays { get; set; }

        // Rewards information
        [MaxLength(4000)]
        public string? RewardsText { get; set; }

        // Image file for contest
        public IFormFile? ImageFile { get; set; }
    }
}
