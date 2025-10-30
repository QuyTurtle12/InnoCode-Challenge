namespace Repository.DTOs.ProblemDTOs
{
    public class GetProblemDTO : BaseProblemDTO
    {
        public Guid ProblemId { get; set; }

        //public string RoundName { get; set; } = string.Empty;

        //public string Type { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
