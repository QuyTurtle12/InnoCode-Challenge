using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.Constant;

namespace Repository.DTOs.MentorManagementDTOs
{
    public class MentorManagerRequestDTO
    {
        [Required, MaxLength(255)]
        public string Fullname { get; set; } = null!;

        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = null!;

        [Required, MinLength(8)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
            ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
        public string Password { get; set; } = null!;

        [Required, Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = null!;
        [RegularExpression(ValidationConstants.VietnamPhoneRegex, ErrorMessage = ValidationConstants.VietnamPhoneErrorMessage)]
        [MaxLength(11)]
        public string? Phone { get; set; }
    }


}

