using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ConfigDTOs
{
    public class CreateConfigDTO
    {
        [Required, MaxLength(120)]
        [RegularExpression(@"^[A-Za-z0-9_.:-]+$", ErrorMessage = "Key can contain letters, digits, _ . : -")]
        public string Key { get; set; } = null!;

        public string? Value { get; set; }

        [MaxLength(50)]
        public string? Scope { get; set; }
    }
}
