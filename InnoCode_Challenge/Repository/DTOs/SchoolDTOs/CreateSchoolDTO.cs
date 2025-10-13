using System.ComponentModel.DataAnnotations;
namespace Repository.DTOs.SchoolDTOs
{
    public class CreateSchoolDTO
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;

        [Required]
        public Guid ProvinceId { get; set; }

        [MaxLength(200)]
        public string? Contact { get; set; }
    }
}
