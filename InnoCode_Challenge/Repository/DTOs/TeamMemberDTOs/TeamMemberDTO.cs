namespace Repository.DTOs.TeamMemberDTOs
{
    public class TeamMemberDTO
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = null!;

        public Guid StudentId { get; set; }
        public string StudentFullname { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;

        public string MemberRole { get; set; } = null!; // Captain | Member
        public DateTime JoinedAt { get; set; }
    }
}
