using System;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.MentorRegistrationDTOs
{
    public class RegisterMentorDTO
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

        [MaxLength(50)]
        public string? Phone { get; set; }

        public Guid? SchoolId { get; set; }

        [MaxLength(200)]
        public string? ProposedSchoolName { get; set; }

        [MaxLength(255)]
        public string? ProposedSchoolAddress { get; set; }

        public Guid? ProvinceId { get; set; }
    }
}
