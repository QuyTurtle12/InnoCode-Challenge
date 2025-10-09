using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AuthDTOs
{
    public class LoginDTO
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; } = null!;
    }
}
