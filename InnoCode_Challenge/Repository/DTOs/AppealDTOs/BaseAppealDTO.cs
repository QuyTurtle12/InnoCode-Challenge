using Utility.Enums;

namespace Repository.DTOs.AppealDTOs
{
    public class BaseAppealDTO
    {
        public Guid TeamId { get; set; }

        public string TargetType { get; set; } = null!;

        public string TargetId { get; set; } = null!;

        public Guid OwnerId { get; set; }

        public string? Reason { get; set; }
    }
}
