namespace Repository.DTOs.ContestDTOs
{
    public class ContestCreatedDTO
    {
        public Guid ContestId { get; set; }
        public int Year { get; set; }
        public string Name { get; set; } = null!;
        public string Status { get; set; } = "draft";
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? imageUrl { get; set; }
        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public int TeamMembersMax { get; set; }
        public int TeamMembersMin { get; set; }
        public int? TeamLimitMax { get; set; }
        public string? RewardsText { get; set; }
    }
}
