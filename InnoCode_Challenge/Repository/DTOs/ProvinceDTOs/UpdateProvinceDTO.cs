using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ProvinceDTOs
{
    public class UpdateProvinceDTO
    {
        [MaxLength(150)]
        public string? Name { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }
    }
}
