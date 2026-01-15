using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ConfigDTOs
{
    public class UpdateConfigDTO
    {
        [MaxLength(4000)]
        public string? Value { get; set; }

        [MaxLength(50)]
        public string? Scope { get; set; }
    }
}
