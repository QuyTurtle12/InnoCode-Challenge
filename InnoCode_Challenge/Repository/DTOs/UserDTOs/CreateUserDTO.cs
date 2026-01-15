using System.ComponentModel.DataAnnotations;
using Utility.Constant;

namespace Repository.DTOs.UserDTOs
{
    public class CreateUserDTO
    {
        [Required, MaxLength(100)]
        public string Fullname { get; set; } = null!;

        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = null!;

        [Required]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
          ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
        public string Password { get; set; } = null!;

        [Required, Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = null!;
        
        [RegularExpression(RoleConstants.RoleRegexPattern, ErrorMessage = RoleConstants.RoleRegexErrorMessage)]
        public string Role { get; set; } = null!;

        [RegularExpression(UserStatusConstants.StatusRegexPattern,
            ErrorMessage = UserStatusConstants.StatusRegexErrorMessage)]
        public string Status { get; set; } = UserStatusConstants.Active;
    }
}
