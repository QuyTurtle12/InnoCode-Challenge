namespace Repository.DTOs.ActivityLogDTOs
{
    public class ActivityLogQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public Guid? UserId { get; set; }
        public string? ActionContains { get; set; }
        public string? TargetType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public string? SortBy { get; set; } // at, action
        public bool Desc { get; set; } = true;
    }
}
