namespace Repository.DTOs.TeamDTOs
{
    public class TeamDTO
    {
        public Guid TeamId { get; set; }

        public string Name { get; set; } = null!;

        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = null!;

        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; } = null!;

        public Guid MentorId { get; set; }
        public string MentorName { get; set; } = null!; // Mentor.User.Fullname

        public DateTime CreatedAt { get; set; }
    }
}
