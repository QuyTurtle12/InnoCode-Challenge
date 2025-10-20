using Microsoft.AspNetCore.Http;

namespace BusinessLogic.IServices.FileStorages
{
    public interface ICloudinaryService
    {
        Task<string> UploadFileAsync(IFormFile file, string folder = "submissions");
        Task<bool> DeleteFileAsync(string publicId);
        string GetDownloadUrl(string publicId);
    }
}
