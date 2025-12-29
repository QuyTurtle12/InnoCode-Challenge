using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamDTOs
{
    public class UpdateTeamDTO
    {
        [MaxLength(150)]
        public string? Name { get; set; }
    }
}
