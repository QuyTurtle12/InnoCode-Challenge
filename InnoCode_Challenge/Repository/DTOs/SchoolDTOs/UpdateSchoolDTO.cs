using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.SchoolDTOs
{
    public class UpdateSchoolDTO
    {
        [MaxLength(150)]
        public string? Name { get; set; }

        public Guid? ProvinceId { get; set; }

        [MaxLength(200)]
        public string? Contact { get; set; }
    }

}
