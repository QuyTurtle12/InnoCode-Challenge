using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TeamDTOs
{
    public class TeamQueryParams
    {
        [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        [Range(1, 200)] public int PageSize { get; set; } = 20;

        public Guid? ContestId { get; set; }
        public Guid? SchoolId { get; set; }
        public Guid? MentorId { get; set; }
        public string? Search { get; set; }          // Name

        // name | createdAt | contestName | schoolName | mentorName
        public string? SortBy { get; set; } = "createdAt";
        public bool Desc { get; set; } = true;
    }
}
