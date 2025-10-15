using System;

namespace Repository.DTOs.MentorRegistrationDTOs
{
    public class MentorRegistrationQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public string? Status { get; set; } // pending/approved/denied
        public string? Search { get; set; } // fullname/email/phone
        public string? SortBy { get; set; } // createdAt, fullname, email, status
        public bool Desc { get; set; } = true;
    }
}