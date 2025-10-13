using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.StudentDTOs
{
    public class UpdateStudentDTO
    {
        public Guid? SchoolId { get; set; }

        [MaxLength(50)]
        public string? Grade { get; set; }
    }
}
