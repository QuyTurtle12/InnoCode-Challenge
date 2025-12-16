using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class SchoolCreationRequestListDTO
    {
        public Guid RequestId { get; set; }
        public Guid RequestedByUserId { get; set; }
        public string Name { get; set; } = null!;
        public Guid ProvinceId { get; set; }
        public string Status { get; set; } = null!;
        public Guid? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? DenyReason { get; set; }
        public Guid? CreatedSchoolId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
