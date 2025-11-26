using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.CertificateDTOs
{
    public class IssueCertificatesDTO
    {
        [Required] public Guid TemplateId { get; set; }
        [Required, MinLength(1)] public List<IssueRecipientDTO> Recipients { get; set; } = new();
        [Required, RegularExpression("^(png|pdf)$")] public string Output { get; set; } = "png";
        public bool Reissue { get; set; } = false;
    }
}
