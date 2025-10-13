using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
