using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class CreateSchoolCreationRequestFormDTO
    {
        [Required] public string Name { get; set; } = null!;
        public string? Address { get; set; }
        [Required] public Guid ProvinceId { get; set; }
        public string? Contact { get; set; }

        public List<IFormFile>? Evidences { get; set; }
    }
}
