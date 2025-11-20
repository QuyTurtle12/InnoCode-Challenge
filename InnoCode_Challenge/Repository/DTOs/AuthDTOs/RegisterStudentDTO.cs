using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.AuthDTOs
{
    namespace Repository.DTOs.AuthDTOs
    {
        public class RegisterStudentDTO
        {
            [Required, MaxLength(100)]
            public string FullName { get; set; } = null!;

            [Required, EmailAddress, MaxLength(255)]
            public string Email { get; set; } = null!;

            [Required, MinLength(8)]
            [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
                ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
            public string Password { get; set; } = null!;

            [Required, Compare("Password", ErrorMessage = "Passwords do not match.")]
            public string ConfirmPassword { get; set; } = null!;

            [Required]
            public Guid SchoolId { get; set; }

            [MaxLength(50)]
            public string? Grade { get; set; }
        }
    }

}
