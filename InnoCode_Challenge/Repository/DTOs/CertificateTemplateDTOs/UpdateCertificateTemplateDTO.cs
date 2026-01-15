using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.CertificateTemplateDTOs
{
    public class UpdateCertificateTemplateDTO
    {
        public string? Name { get; set; }

        public string? FileUrl { get; set; }

        public UpdateTextLayoutDTO? Text { get; set; }
    }
}
