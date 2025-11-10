namespace Repository.DTOs.ContestDTOs
{
    public class JudgeInContestDTO
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Status { get; set; } = null!;
    }
}
