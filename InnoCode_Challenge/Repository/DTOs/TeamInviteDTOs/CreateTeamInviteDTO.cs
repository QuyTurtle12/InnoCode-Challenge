using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamInviteDTOs
{
    public class CreateTeamInviteDTO
    {
        public Guid? StudentId { get; set; }

        [EmailAddress, MaxLength(255)]
        public string? InviteeEmail { get; set; }

        [Range(1, 60)]
        public int? TtlDays { get; set; }
    }
}
