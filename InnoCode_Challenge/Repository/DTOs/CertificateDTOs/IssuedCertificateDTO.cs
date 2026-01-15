using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.CertificateDTOs
{
    public class IssuedCertificateDTO
    {
        public Guid CertificateId { get; set; }
        public Guid TemplateId { get; set; }
        public Guid? TeamId { get; set; }
        public Guid? StudentId { get; set; }
        public string CertificateType { get; set; } = null!;
        public string RecipientName { get; set; } = null!;
        public string FileUrl { get; set; } = null!;
        public DateTime IssuedAt { get; set; }
    }
}
