namespace Repository.DTOs.TeamInviteDTOs
{
    public class TeamInviteDTO
    {
        public Guid InviteId { get; set; }
        public Guid TeamId { get; set; }
        public Guid? StudentId { get; set; }
        public string? InviteeEmail { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public Guid InvitedByUserId { get; set; }

        public string TeamName { get; set; } = null!;
        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = null!;
    }
}
