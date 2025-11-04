using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.ContestDTOs
{
    public class UpdateContestDTO : BaseContestDTO
    {
        //[EnumDataType(typeof(ContestStatusEnum))]
        //[JsonConverter(typeof(JsonStringEnumConverter))]
        //public ContestStatusEnum Status { get; set; } = ContestStatusEnum.Draft;

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
