using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.MentorDTOs
{
    public class UpdateMentorDTO
    {
        public Guid? SchoolId { get; set; }

        [MaxLength(20)]
        [RegularExpression(@"^\+?[0-9]{10,11}$", ErrorMessage = "Phone must be digits (optionally starting with +), length 10-11.")]
        public string? Phone { get; set; }
    }
}
