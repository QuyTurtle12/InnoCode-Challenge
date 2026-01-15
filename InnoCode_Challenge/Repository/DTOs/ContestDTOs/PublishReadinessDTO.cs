namespace Repository.DTOs.ContestDTOs
{
    public class PublishReadinessDTO
    {
        public Guid ContestId { get; set; }
        public bool IsReady { get; set; }
        public List<string> Missing { get; set; } = new();
    }
}
