using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class SchoolCreationRequestEvidenceDTO
    {
        public Guid EvidenceId { get; set; }
        public string Url { get; set; } = null!;
        public string? Type { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
