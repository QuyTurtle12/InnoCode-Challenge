namespace Utility.Constant
{
    public static class RoleConstants
    {
        public const int StudentValue = 1;
        public const int MentorValue = 2;
        public const int JudgeValue = 3;
        public const int AdminValue = 4;
        public const int StaffValue = 5;
        public const int ContestOrganizerValue = 6;

        public const string Student = "Student";
        public const string Mentor = "Mentor";
        public const string Judge = "Judge";
        public const string Admin = "Admin";
        public const string Staff = "Staff";
        public const string ContestOrganizer = "Contest Organizer";

        public static string ToRoleName(int v) =>
            v switch
            {
                AdminValue => Admin,
                StaffValue => Staff,
                MentorValue => Mentor,
                JudgeValue => Judge,
                ContestOrganizerValue => ContestOrganizer,
                _ => Student,
            };

        public static int ToRoleValue(string name) =>
            name switch
            {
                Admin => AdminValue,
                Mentor => MentorValue,
                Staff => StaffValue,
                Judge => JudgeValue,
                ContestOrganizer => ContestOrganizerValue,
                _ => StudentValue
            };
    }
}
