namespace Repository.DTOs.JudgeInviteDTOs
{
    public class JudgeWithInviteStatusDTO
    {
        public Guid JudgeId { get; set; }
        public string JudgeName { get; set; } = null!;
        public string JudgeEmail { get; set; } = null!;
        public string JudgeStatus { get; set; } = null!;

        // Invitation details (null if never invited)
        public Guid? InviteId { get; set; }
        public string? InviteStatus { get; set; }
        public DateTime? InvitedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public string? InviteCode { get; set; }
    }
}
