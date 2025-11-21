using Microsoft.AspNetCore.Http;

namespace Repository.DTOs.SubmissionDTOs
{
    public class CreateSubmissionDTO
    {
        public string? Code { get; set; }
        public IFormFile? File { get; set; }
    }
}
