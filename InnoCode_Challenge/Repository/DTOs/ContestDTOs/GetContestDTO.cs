using System.ComponentModel.DataAnnotations;
using Repository.DTOs.RoundDTOs;

namespace Repository.DTOs.ContestDTOs
{
    public class GetContestDTO : BaseContestDTO
    {
        public Guid ContestId { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedById { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ImgUrl { get; set; } = null!;

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

        public List<GetRoundDTO>? rounds { get; set; } = null;
    }
}
