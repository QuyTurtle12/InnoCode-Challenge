using System.ComponentModel.DataAnnotations;
using Utility.Enums;

namespace Repository.DTOs.ContestDTOs
{
    public class GetContestDTO : BaseContestDTO
    {
        public Guid ContestId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        // Registration timeline
        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }

        // Team configuration
        [Range(1, 50)]
        public int? TeamMembersMax { get; set; }

        [Range(1, 10000)]
        public int? TeamLimitMax { get; set; }

        // Rewards information
        [MaxLength(4000)]
        public string? RewardsText { get; set; }
    }
}
