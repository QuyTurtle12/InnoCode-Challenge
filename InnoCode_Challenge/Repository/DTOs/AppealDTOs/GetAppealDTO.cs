using Utility.Enums;

namespace Repository.DTOs.AppealDTOs
{
    public class GetAppealDTO : BaseAppealDTO
    {
        public Guid AppealId { get; set; }

        public string State { get; set; } = AppealStateEnum.Open.ToString();

        public string TeamName { get; set; } = null!;

        public string OwnerName { get; set; } = null!;

        public string? Decision { get; set; }
    }
}
