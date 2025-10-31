using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AuthDTOs
{
    public class VerifyEmailDTO
    {
        [Required]
        public string Token { get; set; } = null!;
    }
}
