using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AppealEvidenceDTOs
{
    public class AppealEvidenceFileDTO
    {
        [Required(ErrorMessage = "Evidence file is required")]
        public IFormFile File { get; set; } = null!;

        [MaxLength(500, ErrorMessage = "Note cannot exceed 500 characters")]
        public string? Note { get; set; }
    }
}
