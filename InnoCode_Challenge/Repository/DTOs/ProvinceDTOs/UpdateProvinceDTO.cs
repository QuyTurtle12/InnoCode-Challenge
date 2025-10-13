using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
