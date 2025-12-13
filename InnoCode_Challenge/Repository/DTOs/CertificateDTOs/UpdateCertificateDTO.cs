using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.CertificateDTOs
{
    public class UpdateCertificateDTO
    {
        [Url]
        public string? FileUrl { get; set; }

        public DateTime? IssuedAt { get; set; }
    }
}
