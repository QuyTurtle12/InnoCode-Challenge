using System.ComponentModel.DataAnnotations;
using Utility.Constant;

namespace Repository.DTOs.AuthDTOs
{
    public class RegisterMentorDTO
    {
        [Required, MaxLength(100)]
        public string Fullname { get; set; } = null!;

        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = null!;

        [Required, MinLength(8)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
          ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
        public string Password { get; set; } = null!;

        [Required, Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = null!;

        [MaxLength(20)]
        [RegularExpression(ValidationConstants.VietnamPhoneRegex, ErrorMessage = ValidationConstants.VietnamPhoneErrorMessage)]

        public string? Phone { get; set; } 
    }
}
