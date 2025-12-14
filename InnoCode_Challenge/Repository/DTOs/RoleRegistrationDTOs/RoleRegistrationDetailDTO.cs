using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.RoleRegistrationDTOs
{
    public class RoleRegistrationDetailDTO
    {
        public Guid RegistrationId { get; set; }
        public string RequestedRole { get; set; } = null!;
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? Payload { get; set; }
        public string Status { get; set; } = null!;
        public string? DenyReason { get; set; }
        public Guid? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<RoleRegistrationEvidenceDTO> Evidences { get; set; } = new();
    }
}
