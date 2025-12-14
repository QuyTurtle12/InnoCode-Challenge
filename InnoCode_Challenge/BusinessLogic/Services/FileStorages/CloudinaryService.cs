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
        public async Task<string> UploadEvidenceAsync(IFormFile file, string folder = "role_registrations")
        {
            if (file == null || file.Length == 0)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "No file was provided");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".pdf" or ".png" or ".jpg" or ".jpeg"))
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "Evidence must be .pdf, .png, .jpg, or .jpeg");

            if (file.Length > 104857600)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "File size exceeds the limit of 100MB");

            using var stream = file.OpenReadStream();

            if (ext == ".pdf")
            {
                var rawParams = new RawUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    UseFilename = true,
                    UniqueFilename = true,
                    AccessMode = "public",
                    Overwrite = true
                };

                var res = await _cloudinary.UploadAsync(rawParams);
                if (res.Error != null)
                    throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        $"Error uploading file: {res.Error.Message}");

                return res.SecureUrl.ToString();
            }
            else
            {
                var imgParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    UseFilename = true,
                    UniqueFilename = true,
                    Overwrite = true
                };

                var res = await _cloudinary.UploadAsync(imgParams);
                if (res.Error != null)
                    throw new ErrorException(StatusCodes.Status500InternalServerError, ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        $"Error uploading image: {res.Error.Message}");

                return res.SecureUrl?.ToString() ?? res.Url?.ToString() ?? string.Empty;
            }
        }
    }
}
