using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.TestCaseDTOs
{
    public class BulkUpdateTestCaseDTO
    {
        [Required]
        public Guid TestCaseId { get; set; }

        public string? Description { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Weight must be greater than 0")]
        public double Weight { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Time limit must be greater than 0")]
        public int? TimeLimitMs { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Memory limit must be greater than 0")]
        public int? MemoryKb { get; set; }

        public string? Input { get; set; }

        [Required(ErrorMessage = "Expected output is required")]
        public string ExpectedOutput { get; set; } = string.Empty;
    }
}
