using System;

namespace Repository.DTOs.ConfigDTOs
{
    public class ConfigQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? KeyPrefix { get; set; }

        public string? Search { get; set; } // key/value
        public string? Scope { get; set; }
        public string? SortBy { get; set; } // key, updatedAt
        public bool Desc { get; set; } = true;
    }
}