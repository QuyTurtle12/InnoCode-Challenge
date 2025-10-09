using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.UserDTOs
{
    public class CreateUserDTO
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
          ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character."
        )]
        public string Password { get; set; } = null!;

        [Required]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = null!;

        [Required]
        [RegularExpression("^(User|Expert|Staff|Admin)$", ErrorMessage = "Role must be User, Expert,Staff or Admin.")]
        public string Role { get; set; } = null!;

        public string Status { get; set; } = "Active";
    }
}
