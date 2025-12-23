using BusinessLogic.IServices.FileStorages;
using Microsoft.AspNetCore.Http;
using Utility.Constant;
using Utility.ExceptionCustom;

namespace Api.IntegrationTests.Infrastructure
{
    public sealed class FakeCloudinaryService : ICloudinaryService
    {
        public Task<bool> DeleteFileAsync(string publicId)
        {
            return Task.FromResult(true);
        }

        public string GetDownloadUrl(string publicId)
        {
            return publicId;
        }

        public Task<string> UploadFileAsync(IFormFile file, string folder = "others")
        {
            return Task.FromResult(BuildUrl(folder, file?.FileName));
        }

        public Task<string> UploadImageAsync(Stream imageStream, string folder = "certificates", string fileName = "image.png")
        {
            return Task.FromResult(BuildUrl(folder, fileName));
        }

        public Task<string> UploadEvidenceAsync(IFormFile file, string folder = "role_registrations")
        {
            if (file == null || file.Length == 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "No file was provided");
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".pdf" or ".png" or ".jpg" or ".jpeg"))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "Evidence must be .pdf, .png, .jpg, or .jpeg");
            }

            if (file.Length > 104857600)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "File size exceeds the limit of 100MB");
            }

            return Task.FromResult(BuildUrl(folder, file.FileName));
        }

        private static string BuildUrl(string folder, string? fileName)
        {
            var safeFile = string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName;
            return $"https://example.test/{folder}/{Guid.NewGuid():N}_{safeFile}";
        }
    }
}
