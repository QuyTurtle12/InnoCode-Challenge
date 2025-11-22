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
    }
}
