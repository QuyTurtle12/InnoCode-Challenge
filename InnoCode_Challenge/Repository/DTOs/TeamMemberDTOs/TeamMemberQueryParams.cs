using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamMemberDTOs
{
    public class TeamMemberQueryParams
    {
        [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        [Range(1, 200)] public int PageSize { get; set; } = 20;

        public Guid? TeamId { get; set; }
        public Guid? StudentId { get; set; }
        public string? MemberRole { get; set; } // Captain|Member
        public string? Search { get; set; }     // Fullname/Email

        // joinedAt | studentName | teamName | memberRole
        public string? SortBy { get; set; } = "joinedAt";
        public bool Desc { get; set; } = true;
    }
}
