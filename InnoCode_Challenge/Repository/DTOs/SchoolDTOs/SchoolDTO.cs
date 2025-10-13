using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class SchoolDTO
    {
        public Guid SchoolId { get; set; }
        public string Name { get; set; } = null!;
        public Guid ProvinceId { get; set; }
        public string ProvinceName { get; set; } = null!;
        public string? Contact { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

