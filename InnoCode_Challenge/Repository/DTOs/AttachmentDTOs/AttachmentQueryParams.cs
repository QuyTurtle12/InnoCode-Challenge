using System;

namespace Repository.DTOs.AttachmentDTOs
{
    public class AttachmentQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public string? Search { get; set; } // url
        public string? Type { get; set; }
        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }
        public string? SortBy { get; set; } // createdAt, type, url
        public bool Desc { get; set; } = true;
    }
}