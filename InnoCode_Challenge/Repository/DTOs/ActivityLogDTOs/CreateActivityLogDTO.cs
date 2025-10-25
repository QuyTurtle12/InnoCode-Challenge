using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ActivityLogDTOs
{
    public class CreateActivityLogDTO
    {
        [Required]
        public Guid UserId { get; set; }

        [Required, MaxLength(100)]
        public string Action { get; set; } = null!;

        [MaxLength(50)]
        public string? TargetType { get; set; }

        [MaxLength(36)]
        public string? TargetId { get; set; }
    }
}
