using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.RoleRegistrationDTOs
{
    public class RoleRegistrationDTO
    {
        public Guid RegistrationId { get; set; }
        public string RequestedRole { get; set; } = null!;
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string Status { get; set; } = null!;
        public string? DenyReason { get; set; }
        public Guid? ReviewedBy { get; set; }
        public string? ReviewedByName { get; set; }
        public string? ReviewedByEmail { get; set; }

        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public int EvidenceCount { get; set; }
    }
}
