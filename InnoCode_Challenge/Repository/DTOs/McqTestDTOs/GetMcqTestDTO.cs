namespace Repository.DTOs.McqTestDTOs
{
    public class GetMcqTestDTO : BaseMcqTestDTO
    {
        public Guid TestId { get; set; }

        public Guid RoundId { get; set; }
    }
}
