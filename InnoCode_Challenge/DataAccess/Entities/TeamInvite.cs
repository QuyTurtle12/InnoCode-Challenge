namespace DataAccess.Entities
{
    public partial class TeamInvite
    {
        public Guid InviteId { get; set; }

        public Guid TeamId { get; set; }
        public Guid? StudentId { get; set; }         
        public string? InviteeEmail { get; set; }     
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public string Status { get; set; } = null!;   // pending | accepted | cancelled | expired
        public DateTime CreatedAt { get; set; }
        public Guid InvitedByUserId { get; set; }

        public virtual Team Team { get; set; } = null!;
        public virtual Student? Student { get; set; }
        public virtual User InvitedByUser { get; set; } = null!;
    }
}
