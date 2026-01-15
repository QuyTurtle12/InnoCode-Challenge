using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.MentorDTOs
{
    public class MentorQueryParams
    {
        [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        [Range(1, 200)] public int PageSize { get; set; } = 20;

        public Guid? SchoolId { get; set; }
        public Guid? UserId { get; set; }
        public string? Search { get; set; }        // Fullname/Email/Phone
        
        // createdAt | userName | schoolName | phone
        public string? SortBy { get; set; } = "createdAt";
        public bool Desc { get; set; } = true;
    }
}
