using Repository.DTOs.AppealEvidenceDTOs;
using Utility.Enums;

namespace Repository.DTOs.AppealDTOs
{
    public class GetAppealDTO
    {
        public Guid AppealId { get; set; }
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = string.Empty;
        public Guid RoundId { get; set; }
        public string RoundName { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public Guid OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public Guid MentorId { get; set; }
        public string MentorName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? Decision { get; set; }
        public string? DecisionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<AppealEvidenceDTO> Evidences { get; set; } = new();
    }
}
