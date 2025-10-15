namespace DataAccess.Entities
{
    public partial class MentorRegistration
    {
        public Guid RegistrationId { get; set; }

        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? PasswordHash { get; set; }
        public string? Phone { get; set; }

        public Guid? SchoolId { get; set; }
        public string? ProposedSchoolName { get; set; }
        public string? ProposedSchoolAddress { get; set; }
        public Guid? ProvinceId { get; set; }

        public string Status { get; set; } = null!;         // pending | approved | denied
        public string? DenyReason { get; set; }
        public Guid? ReviewedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        public virtual School? School { get; set; }
        public virtual Province? Province { get; set; }
        public virtual User? ReviewedByUser { get; set; }
    }
}
