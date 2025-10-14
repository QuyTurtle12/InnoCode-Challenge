namespace Repository.DTOs.SubmissionDTOs
{
    public class BaseSubmissionDTO
    {
        public Guid TeamId { get; set; }

        public Guid ProblemId { get; set; }

        public Guid? SubmittedByStudentId { get; set; }

        public string? JudgedBy { get; set; }

        public string? Status { get; set; }

        public double Score { get; set; }
    }
}
