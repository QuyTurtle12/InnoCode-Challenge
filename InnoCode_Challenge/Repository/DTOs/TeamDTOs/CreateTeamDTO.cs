using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamDTOs
{
    public class CreateTeamDTO
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;

        [Required]
        public Guid ContestId { get; set; }

        [Required]
        public Guid SchoolId { get; set; }

        [Required]
        public Guid MentorId { get; set; }
    }
}
