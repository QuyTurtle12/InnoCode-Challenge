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
        public static string ContestJudge(Guid contestId, Guid judgeUserId) => $"contest:{contestId}:judge:{judgeUserId}";
        public static string ContestPolicy(Guid contestId, string policyKey) => $"contest:{contestId}:policy:{policyKey}";
        public static string ContestPolicyPrefix(Guid contestId) => $"contest:{contestId}:policy:";
        public static string JudgeSubmission(Guid judgeUserId, Guid submissionId) => $"judge:{judgeUserId}:submission:{submissionId}";
        public static string RoundSubmissionsDistributed(Guid roundId) => $"round:{roundId}:submissions_distributed";

        public static string RoundTimeLimitSeconds(Guid roundId) => $"contest:round:{roundId}:time_limit_seconds";
        public static string McqTestImportTemplate() => $"template:McqImportTemplate:mcq_test";
        public static string AutoTestImportTemplate() => $"template:TestCaseImportTemplate:auto_evaluation_test";
        public static string ManualTestImportTemplate() => $"template:RubricImportTemplate:manual_test";

    }
}
