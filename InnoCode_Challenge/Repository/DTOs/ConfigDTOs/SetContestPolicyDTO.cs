using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ConfigDTOs
{
    public class SetContestPolicyDTO
    {
        [Range(1, 20)]
        public int? TeamMembersMax { get; set; }

        [Range(1, 60)]
        public int? TeamInviteTtlDays { get; set; }
    }
}
