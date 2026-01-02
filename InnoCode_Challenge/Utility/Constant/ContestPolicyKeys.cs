using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility.Constant
{
    public static class ContestPolicyKeys
    {
        public const string TieBreakRule = "tie_break_rule";
        public const string EliminationRule = "elimination_rule";

        public const string MaxSubmissionsPerProblem = "max_submissions_per_problem";
        public const string AllowNegativeScore = "allow_negative_score";
        public const string AllowLateSubmission = "allow_late_submission";

        public const string AppealSubmitDays = "appeal_submit_days";
        public const string AppealReviewDays = "appeal_review_days";
        public const string JudgeRescoreDays = "judge_rescore_days";
    }
}
