using System;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.AttachmentDTOs
{
    public class UpdateAttachmentDTO
    {
        [Url, MaxLength(1000)]
        public string? Url { get; set; }

        [MaxLength(100)]
        public string? Type { get; set; }
    }
}
