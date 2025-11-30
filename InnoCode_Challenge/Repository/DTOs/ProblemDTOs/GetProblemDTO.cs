namespace Repository.DTOs.ProblemDTOs
{
    public class GetProblemDTO : BaseProblemDTO
    {
        public Guid ProblemId { get; set; }

        public string? TemplateUrl { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
