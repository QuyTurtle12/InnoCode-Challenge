namespace Utility.Constant
{
    public static class RoleConstants
    {
        public const string Student = "Student";
        public const string Mentor = "Mentor";
        public const string Judge = "Judge";
        public const string Admin = "Admin";
        public const string Staff = "Staff";
        public const string ContestOrganizer = "Contest Organizer";

        public const string RoleRegexPattern =
        "^(" + Student + "|" + Mentor + "|" + Judge + "|" + Staff + "|" + Admin + "|" + ContestOrganizer + ")$";

        public const string RoleRegexErrorMessage =
            "Role must be one of: " + Student + ", " + Mentor + ", " + Judge + ", " + Staff + ", " + Admin + ", " + ContestOrganizer + ".";

    }
}
