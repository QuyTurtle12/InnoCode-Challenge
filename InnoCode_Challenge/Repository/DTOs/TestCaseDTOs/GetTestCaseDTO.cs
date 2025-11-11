namespace Repository.DTOs.TestCaseDTOs
{
    public class GetTestCaseDTO : BaseTestCaseDTO
    {
        public Guid ProblemId { get; set; }
        public Guid TestCaseId { get; set; }

        public string Type { get; set; } = string.Empty!;
    }
}
