namespace Repository.DTOs.TeamInviteDTOs
{
    public class TeamInviteQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Status { get; set; } // pending, accepted, declined, revoked, expired
        public string? Search { get; set; } // email contains
        public string? SortBy { get; set; } // createdAt, expiresAt, email, status
        public bool Desc { get; set; } = true;
    }
}
