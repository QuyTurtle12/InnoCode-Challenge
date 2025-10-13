using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
