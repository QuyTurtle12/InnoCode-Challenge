using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AuthDTOs
{
    public class RegisterUserDTO
    {
        [Required, MaxLength(100)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress]
        [RegularExpression(
          @"^[a-zA-Z0-9._%+-]+@gmail\.com$", ErrorMessage = "Email must be a valid Gmail address."
        )]
        public string Email { get; set; } = null!;

        [Required]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [RegularExpression(
          @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
          ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit, and one special character."
        )]
        public string Password { get; set; } = null!;

        [Required]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
