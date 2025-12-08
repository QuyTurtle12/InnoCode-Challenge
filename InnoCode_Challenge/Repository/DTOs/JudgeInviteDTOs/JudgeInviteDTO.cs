namespace Repository.DTOs.JudgeInviteDTOs
{
    public class JudgeInviteDTO
    {
        public Guid InviteId { get; set; }
        public Guid JudgeId { get; set; }
        public string JudgeName { get; set; } = null!;
        public string JudgeEmail { get; set; } = null!;
        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = null!;
        public string InviteCode { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public string? CreatedBy { get; set; }
    }
}
