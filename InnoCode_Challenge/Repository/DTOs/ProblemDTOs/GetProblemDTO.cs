using Utility.Enums;

namespace Repository.DTOs.ProblemDTOs
{
    public class GetProblemDTO : BaseProblemDTO
    {
        public Guid ProblemId { get; set; }

        public string? TemplateUrl { get; set; }

        public string? Type { get; set; }

        public string? MockTestUrl { get; set; }

        public string? TestType { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
