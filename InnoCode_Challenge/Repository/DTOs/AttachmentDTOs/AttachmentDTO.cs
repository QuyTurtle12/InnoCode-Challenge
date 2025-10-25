using System;

namespace Repository.DTOs.AttachmentDTOs
{
    public class AttachmentDTO
    {
        public Guid AttachmentId { get; set; }
        public string Url { get; set; } = null!;
        public string? Type { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
