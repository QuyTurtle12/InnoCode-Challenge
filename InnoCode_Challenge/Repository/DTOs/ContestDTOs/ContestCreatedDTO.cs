namespace Repository.DTOs.ContestDTOs
{
    public class ContestCreatedDTO
    {
        public Guid ContestId { get; set; }
        public int Year { get; set; }
        public string Name { get; set; } = null!;
        public string Status { get; set; } = "draft";
        public DateTime CreatedAt { get; set; }

        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }
        public int TeamMembersMax { get; set; }
        public int? TeamLimitMax { get; set; }
        public string? RewardsText { get; set; }
    }
}
