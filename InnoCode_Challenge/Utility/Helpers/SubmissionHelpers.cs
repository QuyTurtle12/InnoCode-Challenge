namespace Utility.Helpers
{
    public static class SubmissionHelpers
    {
        public static int ConvertToJudge0LanguageId(string language)
        {
            return language.ToLower() switch
            {
                "python3" => 71,
                "python" => 70,
                _ => 71 // Default to Python3
            };
        }

        public static string ConvertIdToJudge0Language(int language)
        {
            return language switch
            {
                71 => "python3",
                70 => "python",
                _ => "python3" // Default to Python3
            };
        }

        public static int? ParseRuntime(string? time)
        {
            if (string.IsNullOrEmpty(time)) return null;

            if (double.TryParse(time, out double runtime))
            {
                return (int)(runtime * 1000); // Convert to milliseconds
            }

            return null;
        }
    }
}
