using System;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AttachmentDTOs
{
    public class CreateAttachmentDTO
    {
        [Required, Url, MaxLength(1000)]
        public string Url { get; set; } = null!;

        [MaxLength(100)]
        public string? Type { get; set; }
    }
}
