namespace Repository.DTOs.RoundDTOs
{
    public class BaseRoundDTO
    {
        public string Name { get; set; } = null!;

        public DateTime Start { get; set; }

        public DateTime End { get; set; }
    }
}
