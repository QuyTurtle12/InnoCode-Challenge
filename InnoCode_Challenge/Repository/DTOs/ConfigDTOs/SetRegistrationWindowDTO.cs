using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ConfigDTOs
{
    public class SetRegistrationWindowDTO
    {
        [Required] public DateTime RegistrationStartUtc { get; set; }
        [Required] public DateTime RegistrationEndUtc { get; set; }
    }
}
