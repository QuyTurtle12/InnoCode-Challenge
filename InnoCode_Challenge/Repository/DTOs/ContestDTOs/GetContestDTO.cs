using Utility.Enums;

namespace Repository.DTOs.ContestDTOs
{
    public class GetContestDTO : BaseContestDTO
    {
        public Guid ContestId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
