using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.UserDTOs
{
    public class CurrentProfileDTO
    {
        public string UserId { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string FullName { get; set; } = default!;
        public string Role { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // StudentProfileDetailsDTO | MentorProfileDetailsDTO | null
        public object? Details { get; set; }
    }
}
