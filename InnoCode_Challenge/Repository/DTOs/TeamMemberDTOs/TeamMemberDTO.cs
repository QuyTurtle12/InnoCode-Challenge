using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.TeamMemberDTOs
{
    public class TeamMemberDTO
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = null!;

        public Guid StudentId { get; set; }
        public string StudentFullname { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;

        [Required]
        [EnumDataType(typeof(MemberRoleEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MemberRoleEnum MemberRole { get; set; } = MemberRoleEnum.Member;
        public DateTime JoinedAt { get; set; }
    }
}
