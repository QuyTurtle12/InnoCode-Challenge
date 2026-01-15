using Repository.DTOs.TeamMemberDTOs;

namespace Repository.DTOs.TeamDTOs
{
    public class TeamWithMembersDTO
    {
        public Guid TeamId { get; set; }
        public string Name { get; set; } = null!;

        public Guid ContestId { get; set; }
        public string ContestName { get; set; } = null!;

        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; } = null!;

        public Guid MentorId { get; set; }
        public string MentorName { get; set; } = null!;

        public DateTime CreatedAt { get; set; }
        public IList<TeamMemberDTO> Members { get; set; } = new List<TeamMemberDTO>();
    }
}
