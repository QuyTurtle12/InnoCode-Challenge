using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AuthDTOs
{
    public class RefreshRequestDTO
    {
        [Required] public string RefreshToken { get; set; } = null!;
    }

}
