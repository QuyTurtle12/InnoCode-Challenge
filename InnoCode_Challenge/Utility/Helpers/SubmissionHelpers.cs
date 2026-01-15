using Microsoft.AspNetCore.Http;
using Utility.Constant;
using Utility.ExceptionCustom;

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

        public static async Task<string> DownloadFileContentAsync(string fileUrl)
        {
            try
            {
                using HttpClient httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                HttpResponseMessage response = await httpClient.GetAsync(fileUrl);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Failed to download file from URL: {ex.Message}");
            }
        }
    }
}
