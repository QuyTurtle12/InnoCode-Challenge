using System.ComponentModel.DataAnnotations;
using Utility.Constant;

namespace Repository.DTOs.UserDTOs
{
    public class UpdateUserDTO
    {
        [Required]
        public Guid Id { get; set; }

        [MaxLength(100)]
        public string? Fullname { get; set; }

        [EmailAddress, MaxLength(255)]
        public string? Email { get; set; }

        [RegularExpression(RoleConstants.RoleRegexPattern, ErrorMessage = RoleConstants.RoleRegexErrorMessage)]
        public string? Role { get; set; }

        [RegularExpression("^(Active|Inactive|Locked)$",
            ErrorMessage = "Status must be one of: Active, Inactive, Locked.")]
        public string? Status { get; set; }

        [MinLength(8)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
    ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
        public string? NewPassword { get; set; }

        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmNewPassword { get; set; }

    }
}
