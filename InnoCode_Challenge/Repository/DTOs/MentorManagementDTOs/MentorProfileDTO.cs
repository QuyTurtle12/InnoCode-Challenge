using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.MentorManagementDTOs
{
    public class MentorProfileDTO
    {
        public Guid MentorId { get; set; }
        public Guid UserId { get; set; }
        public Guid SchoolId { get; set; }
        public string Email { get; set; } = null!;
        public string Fullname { get; set; } = null!;
        public string Role { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
    }
}
