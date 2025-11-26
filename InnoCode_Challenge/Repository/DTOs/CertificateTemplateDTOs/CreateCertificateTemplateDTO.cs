using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.CertificateTemplateDTOs
{
    public class CreateCertificateTemplateDTO
    {
        [Required] public Guid ContestId { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; } = null!;
        [Required, Url] public string FileUrl { get; set; } = null!;
        [Required] public TextLayoutDTO Text { get; set; } = new();
    }
}
