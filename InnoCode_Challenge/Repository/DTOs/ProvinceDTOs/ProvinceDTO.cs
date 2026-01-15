namespace Repository.DTOs.ProvinceDTOs
{
    public class ProvinceDTO
    {
        public Guid ProvinceId { get; set; }
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
    }
}
