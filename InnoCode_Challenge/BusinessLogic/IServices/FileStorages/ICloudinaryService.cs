using Microsoft.AspNetCore.Http;

namespace BusinessLogic.IServices.FileStorages
{
    public interface ICloudinaryService
    {
        Task<string> UploadFileAsync(IFormFile file, string folder = "others");
        Task<bool> DeleteFileAsync(string publicId);
    }
}
