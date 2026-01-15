using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamMemberDTOs
{
    public class UpdateTeamMemberDTO
    {
        [Required]
        [RegularExpression("^(Captain|Member)$", ErrorMessage = "MemberRole must be Captain or Member.")]
        public string MemberRole { get; set; } = null!;
    }
}
