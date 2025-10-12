using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.UserDTOs
{
    using System.ComponentModel.DataAnnotations;

    namespace Repository.DTOs.UserDTOs
    {
        public class UserQueryParams
        {
            [Range(1, int.MaxValue)]
            public int Page { get; set; } = 1;

            [Range(1, 200)]
            public int PageSize { get; set; } = 20;
            public string? Search { get; set; }        // fullname/email   
            public string? Role { get; set; }             
            public string? Status { get; set; }           

            // createdAt | updatedAt | fullname | email | role | status
            public string? SortBy { get; set; } = "createdAt";
            public bool Desc { get; set; } = true;
        }
    }

}
