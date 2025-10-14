namespace Repository.DTOs.McqAttemptDTOs
{
    public class GetMcqAttemptDTO : BaseMcqAttemptDTO
    {
        public Guid AttemptId { get; set; }

        public Guid TestId { get; set; }

        public string TestName { get; set; } = string.Empty;

        public Guid RoundId { get; set; }

        public string RoundName { get; set; } = string.Empty;

        public Guid StudentId { get; set; }

        public string StudentName { get; set; } = string.Empty;
    }
}
