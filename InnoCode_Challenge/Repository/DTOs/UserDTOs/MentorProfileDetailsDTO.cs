using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.UserDTOs
{
    public class MentorProfileDetailsDTO
    {
        public string MentorId { get; set; } = default!;
        public string SchoolId { get; set; } = default!;
        public string SchoolName { get; set; } = default!;
        public string Province { get; set; } = default!;
        public string? Phone { get; set; }
    }
}
