using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.ProvinceDTOs
{
    public class ProvinceDTO
    {
        public Guid ProvinceId { get; set; }
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
    }
}
