namespace Utility.Constant
{
    public static class ConfigKeys
    {
        public const string Defaults_TeamMembersMax = "defaults:team_members_max";      
        public const string Defaults_TeamInviteTtlDays = "defaults:team_invite_ttl_days";   
        public const string Defaults_TeamLimitMax = "defaults:team_limit_max";         

        public static string ContestTeamMembersMax(Guid contestId) => $"contest:{contestId}:team_members_max";
        public static string ContestTeamLimitMax(Guid contestId) => $"contest:{contestId}:team_limit_max";
        public static string ContestInviteTtlDays(Guid contestId) => $"contest:{contestId}:invite_ttl_days";
        public static string ContestRegStart(Guid contestId) => $"contest:{contestId}:registration_start";
        public static string ContestRegEnd(Guid contestId) => $"contest:{contestId}:registration_end";
        public static string ContestRewards(Guid contestId) => $"contest:{contestId}:rewards_text";
    }
}
