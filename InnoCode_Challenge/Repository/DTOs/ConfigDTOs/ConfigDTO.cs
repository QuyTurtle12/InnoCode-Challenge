namespace Repository.DTOs.ConfigDTOs
{
    public class ConfigDTO
    {
        public string Key { get; set; } = null!;
        public string? Value { get; set; }
        public string? Scope { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
