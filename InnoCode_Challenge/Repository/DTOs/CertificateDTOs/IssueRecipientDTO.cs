using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.CertificateDTOs
{
    public class IssueRecipientDTO
    {
        public Guid? TeamId { get; set; }
        public Guid? StudentId { get; set; }
        public string? DisplayName { get; set; }
    }
}
