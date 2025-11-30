using Microsoft.AspNetCore.Http;

namespace Utility.Helpers
{
    public static class CloudinaryHelpers
    {
        public static bool IsImageFile(IFormFile file)
        {
            string[] permittedExtensions = { ".jpg", ".jpeg", ".png" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            return !string.IsNullOrEmpty(ext) && permittedExtensions.Contains(ext);
        }

        public static string? ExtractCloudinaryPublicId(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return null;
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                const string marker = "/upload/";
                int idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return null;

                string after = path[(idx + marker.Length)..];
                // remove possible version prefix
                if (after.StartsWith("v") && after.Length > 2)
                {
                    int slash = after.IndexOf('/');
                    if (slash > 1)
                    {
                        string maybeVersion = after.Substring(1, slash - 1);
                        if (int.TryParse(maybeVersion, out _))
                        {
                            after = after[(slash + 1)..];
                        }
                    }
                }

                // remove leading slash if any
                if (after.StartsWith("/")) after = after[1..];

                // remove extension
                int lastDot = after.LastIndexOf('.');
                string publicId = lastDot > 0 ? after.Substring(0, lastDot) : after;

                // ensure no leading slash
                if (publicId.StartsWith("/")) publicId = publicId[1..];

                return publicId;
            }
            catch
            {
                return null;
            }
        }
    }
}
