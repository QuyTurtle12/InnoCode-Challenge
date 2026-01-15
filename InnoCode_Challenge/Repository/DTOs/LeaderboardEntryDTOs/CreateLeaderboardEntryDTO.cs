namespace Repository.DTOs.LeaderboardEntryDTOs
{
    public class CreateLeaderboardEntryDTO : BaseLeaderboardEntryDTO
    {
        public IList<Guid> teamIdList { get; set; } = new List<Guid>();
    }
}
