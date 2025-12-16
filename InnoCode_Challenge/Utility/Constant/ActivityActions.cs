namespace Utility.Constant
{
    public static class ActivityActions
    {
        // Auth / profile
        public const string UserLogin = "user.login";
        public const string UserLogout = "user.logout";
        public const string UserRegister = "user.register";
        public const string UserProfileUpdate = "user.profile_update";
        public const string UserPasswordChange = "user.password_change";

        // Contest
        public const string ContestCreate = "contest.create";
        public const string ContestUpdate = "contest.update";
        public const string ContestPublish = "contest.publish";
        public const string ContestCancel = "contest.cancel";
        public const string ContestStartNow = "contest.start_now";
        public const string ContestEndNow = "contest.end_now";

        // Round
        public const string RoundCreate = "round.create";
        public const string RoundUpdate = "round.update";
        public const string RoundDelete = "round.delete";
        public const string RoundStartNow = "round.start_now";
        public const string RoundEndNow = "round.end_now";

        // Team
        public const string TeamCreate = "team.create";
        public const string TeamUpdate = "team.update";
        public const string TeamMemberAdd = "team.member_add";
        public const string TeamMemberRemove = "team.member_remove";

        // Submission
        public const string SubmissionCreate = "submission.create";
        public const string SubmissionRerun = "submission.rerun";
        public const string SubmissionOverride = "submission.override";
        public const string SubmissionStatusChange = "submission.status_change";
        public const string SubmissionAssignJudge = "submission.assign_judge";

        // Team invite
        public const string TeamInviteCreated = "team_invite.created";
        public const string TeamInviteResent = "team_invite.resent";
        public const string TeamInviteRevoked = "team_invite.revoked";
        public const string TeamInviteAccepted = "team_invite.accepted";
        public const string TeamInviteDeclined = "team_invite.declined";

        // Notifications
        public const string NotificationCreated = "notification.created";

        // Appeal
        public const string AppealSubmit = "appeal.submit";
        public const string AppealStatusChange = "appeal.status_change";
        public const string AppealResolve = "appeal.resolve";

        // Certificates
        public const string CertTemplateCreate = "cert_template.create";
        public const string CertTemplateUpdate = "cert_template.update";
        public const string CertTemplateDelete = "cert_template.delete";
        public const string CertificateIssue = "certificate.issue";
        public const string CertificateReissue = "certificate.reissue";

        // Admin
        public const string AdminConfigChange = "admin.config_change";

        public const string SchoolRequestCreate = "school_request.create";
        public const string SchoolRequestApprove = "school_request.approve";
        public const string SchoolRequestDeny = "school_request.deny";

        public const string RoleRegistrationApprove = "role_registration.approve";
        public const string RoleRegistrationDeny = "role_registration.deny";

    }

}
