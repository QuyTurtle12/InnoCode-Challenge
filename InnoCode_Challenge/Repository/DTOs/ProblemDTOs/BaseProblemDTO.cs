namespace Repository.DTOs.ProblemDTOs
{
    public class BaseProblemDTO
    {

        public string Description { get; set; } = string.Empty;

        public string Language { get; set; } = null!;

        public double? PenaltyRate { get; set; }
    }
}
