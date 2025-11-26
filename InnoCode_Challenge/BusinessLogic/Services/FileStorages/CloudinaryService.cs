using BusinessLogic.IServices.FileStorages;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.Helpers;
using CloudinaryDotNet.Actions;

namespace BusinessLogic.Services.FileStorages
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(IOptions<CloudinarySettings> config)
        {
            var acc = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );

            _cloudinary = new Cloudinary(acc);
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folder = "submissions")
        {
            if (file == null || file.Length == 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "No file was provided");
            }

            // Check file extension
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".zip" &&
                extension != ".rar" &&
                extension != ".jpeg" &&
                extension != ".jpg" &&
                extension != ".pdf" &&
                extension != ".png" &&
                extension != ".py" &&
                extension != ".python" &&
                extension != ".txt")
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Only .zip, .rar, .pdf, .png, .jpg, .jpeg, .py, .python, or .txt files are allowed");
            }

            // Check file size (limit to 100MB)
            if (file.Length > 104857600) // 100MB in bytes
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "File size exceeds the limit of 100MB");
            }

            using var stream = file.OpenReadStream();
            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true,
                AccessMode = "public",
                Overwrite = true
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error uploading file: {uploadResult.Error.Message}");
            }

            return uploadResult.SecureUrl.ToString();
        }

        public async Task<bool> DeleteFileAsync(string publicId)
        {
            var deleteParams = new DeletionParams(publicId)
            {
                ResourceType = ResourceType.Raw
            };

            var result = await _cloudinary.DestroyAsync(deleteParams);
            return result.Result == "ok";
        }

        public string GetDownloadUrl(string publicId)
        {
            return publicId;
        }

        public async Task<string> UploadImageAsync(Stream imageStream, string folder = "certificates", string fileName = "image.png")
        {
            if (imageStream == null || !imageStream.CanRead)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "No image stream provided");

            imageStream.Position = 0;

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, imageStream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = true,
                Format = "png" 
            };

            var res = await _cloudinary.UploadAsync(uploadParams);
            if (res.Error != null)
                throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Cloudinary upload error: {res.Error.Message}");

            return res.SecureUrl?.ToString() ?? res.Url?.ToString() ?? string.Empty;
        }

    }
}
