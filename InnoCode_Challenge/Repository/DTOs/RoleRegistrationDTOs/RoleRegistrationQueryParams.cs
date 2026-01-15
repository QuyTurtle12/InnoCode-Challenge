using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.RoleRegistrationDTOs
{
    public class RoleRegistrationQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public string? Status { get; set; }          // pending/approved/denied
        public string? RequestedRole { get; set; }   // staff/organizer/judge
        public string? EmailContains { get; set; }

        public string? SortBy { get; set; } // createdAt, email, status, role
        public bool Desc { get; set; } = true;
    }
}
