namespace Repository.DTOs.McqAttemptDTOs
{
    public class CreateMcqAttemptDTO : BaseMcqAttemptDTO
    {
        public Guid TestId { get; set; }

        public Guid RoundId { get; set; }

        public Guid StudentId { get; set; }
    }
}
