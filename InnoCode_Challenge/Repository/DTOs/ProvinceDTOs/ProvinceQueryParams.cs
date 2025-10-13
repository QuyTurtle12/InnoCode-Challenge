using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ProvinceDTOs
{
    public class ProvinceQueryParams
    {
        [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        [Range(1, 200)] public int PageSize { get; set; } = 20;

        public string? Search { get; set; }         // contains on Name/Address
        public string? SortBy { get; set; } = "name"; // name|address
        public bool Desc { get; set; } = false;
    }
}
