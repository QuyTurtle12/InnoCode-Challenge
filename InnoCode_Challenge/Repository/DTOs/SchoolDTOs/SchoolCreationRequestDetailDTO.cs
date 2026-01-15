using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class SchoolCreationRequestDetailDTO : SchoolCreationRequestListDTO
    {
        public string? Address { get; set; }
        public string? Contact { get; set; }
        public List<SchoolCreationRequestEvidenceDTO> Evidences { get; set; } = new();
    }
}
