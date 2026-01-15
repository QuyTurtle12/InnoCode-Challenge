using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class SchoolCreationRequestQueryParams
    {
        public string? Status { get; set; }
        public string? Search { get; set; } // name/contact
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
