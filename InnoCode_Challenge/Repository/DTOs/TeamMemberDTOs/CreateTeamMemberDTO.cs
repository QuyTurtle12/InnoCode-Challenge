using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamMemberDTOs
{
    public class CreateTeamMemberDTO
    {
        [Required]
        public Guid TeamId { get; set; }

        [Required]
        public Guid StudentId { get; set; }

        [RegularExpression("^(Captain|Member)$", ErrorMessage = "MemberRole must be Captain or Member.")]
        public string? MemberRole { get; set; } = "Member";
    }
}
