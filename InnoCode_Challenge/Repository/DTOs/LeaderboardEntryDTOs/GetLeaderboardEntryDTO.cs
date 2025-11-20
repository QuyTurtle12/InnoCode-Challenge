namespace Repository.DTOs.LeaderboardEntryDTOs
{
    public class GetLeaderboardEntryDTO : BaseLeaderboardEntryDTO
    {
        public Guid EntryId { get; set; }

        public string ContestName { get; set; } = string.Empty;

        public IList<TeamInfo> teamIdList { get; set; } = new List<TeamInfo>();

        public DateTime SnapshotAt { get; set; }

        public int TotalTeamCount { get; set; }
    }

    public class TeamInfo
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        public int Rank { get; set; } = 0;

        public double Score { get; set; } = 0;

        public IList<MemberInfo> Members { get; set; } = new List<MemberInfo>();
    }

    public class MemberInfo
    {
        public Guid MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string? MemberRole { get; set; }
        public double TotalScore { get; set; } = 0;

        public IList<RoundScoreDetail> RoundScores { get; set; } = new List<RoundScoreDetail>();
    }

    public class RoundScoreDetail
    {
        public Guid RoundId { get; set; }
        public string RoundName { get; set; } = string.Empty;
        public double Score { get; set; } = 0;
        public string RoundType { get; set; } = string.Empty;
        public DateTime? CompletedAt { get; set; }
    }
}
