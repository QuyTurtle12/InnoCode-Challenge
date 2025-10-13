using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.SchoolDTOs
{
    public class SchoolQueryParams
    {
        [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        [Range(1, 200)] public int PageSize { get; set; } = 20;

        public string? Search { get; set; }          // Search Name/Contact
        public Guid? ProvinceId { get; set; }
        public string? SortBy { get; set; } = "name"; // name | createdAt | provinceName
        public bool Desc { get; set; } = false;
    }
}
