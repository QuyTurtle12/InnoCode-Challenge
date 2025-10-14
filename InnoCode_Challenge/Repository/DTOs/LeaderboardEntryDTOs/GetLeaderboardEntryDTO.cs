namespace Repository.DTOs.LeaderboardEntryDTOs
{
    public class GetLeaderboardEntryDTO : BaseLeaderboardEntryDTO
    {
        public Guid EntryId { get; set; }

        public string ContestName { get; set; } = string.Empty;

        public IList<TeamInfo> teamIdList { get; set; } = new List<TeamInfo>();

        public DateTime SnapshotAt { get; set; }
    }

    public class TeamInfo
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        public int Rank { get; set; } = 0;

        public double Score { get; set; } = 0;
    }
}
