namespace Repository.DTOs.ProblemDTOs
{
    public class BaseProblemDTO
    {
        public Guid RoundId { get; set; }

        public string Language { get; set; } = null!;

        public double? PenaltyRate { get; set; }
    }
}
