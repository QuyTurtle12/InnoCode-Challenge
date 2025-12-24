namespace Utility.Helpers
{
    public static class EmailHelper
    {
        public static string Normalize(string email)
            => (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
