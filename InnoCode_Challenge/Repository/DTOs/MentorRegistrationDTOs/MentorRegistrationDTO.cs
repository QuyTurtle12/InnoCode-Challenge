namespace Repository.DTOs.MentorRegistrationDTOs
{
    public class MentorRegistrationDTO
    {
        public Guid RegistrationId { get; set; }
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }

        public Guid? SchoolId { get; set; }
        public string? ProposedSchoolName { get; set; }
        public string? ProposedSchoolAddress { get; set; }
        public Guid? ProvinceId { get; set; }

        public string Status { get; set; } = null!;
        public string? DenyReason { get; set; }
        public Guid? ReviewedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }
}
