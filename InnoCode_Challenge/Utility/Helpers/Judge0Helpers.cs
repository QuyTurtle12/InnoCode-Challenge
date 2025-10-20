using Utility.Enums;

namespace Utility.Helpers
{
    public static class Judge0Helpers
    {
        public static string ConvertToJudge0StatusString(int? statusId)
        {
            if (!statusId.HasValue)
                return string.Empty;

            if (Enum.IsDefined(typeof(Judge0StatusEnum), statusId.Value))
                return ((Judge0StatusEnum)statusId.Value).ToString();

            return $"Unknown({statusId})";
        }
    }
}
