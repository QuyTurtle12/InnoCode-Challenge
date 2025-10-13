using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.StudentDTOs
{
    public class CreateStudentDTO
    {
        [Required]
        public Guid UserId { get; set; }

        [Required]
        public Guid SchoolId { get; set; }

        [MaxLength(50)]
        public string? Grade { get; set; }
    }
}
