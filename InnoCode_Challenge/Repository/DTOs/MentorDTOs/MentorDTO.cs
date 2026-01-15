namespace Repository.DTOs.MentorDTOs
{
    public class MentorDTO
    {
        public Guid MentorId { get; set; }
        public Guid UserId { get; set; }
        public string UserFullname { get; set; } = null!;
        public string UserEmail { get; set; } = null!;
        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; } = null!;
        public string? Phone { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
