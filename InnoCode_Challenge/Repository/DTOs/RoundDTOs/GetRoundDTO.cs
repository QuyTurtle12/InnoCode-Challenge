namespace Repository.DTOs.RoundDTOs
{
    public class GetRoundDTO : BaseRoundDTO
    {
        public Guid RoundId { get; set; }

        public Guid ContestId { get; set; }

        public string RoundName { get; set; } = string.Empty;

        public string ContestName { get; set; } = string.Empty;
    }
}
