using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ConfigDTOs
{
    public class UpdateConfigDTO
    {
        public string? Value { get; set; }

        [MaxLength(50)]
        public string? Scope { get; set; }
    }
}
