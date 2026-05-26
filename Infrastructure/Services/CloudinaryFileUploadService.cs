using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Domain.Interfaces;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class CloudinaryFileUploadService : IFileUploadService
{
    private readonly Cloudinary? _cloudinary;
    private readonly ILogger<CloudinaryFileUploadService> _logger;

    public CloudinaryFileUploadService(
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryFileUploadService> logger)
    {
        _logger = logger;

        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.CloudName)
            || string.IsNullOrWhiteSpace(o.ApiKey)
            || string.IsNullOrWhiteSpace(o.ApiSecret))
        {
            _cloudinary = null;
            return;
        }

        _cloudinary = new Cloudinary(new Account(o.CloudName, o.ApiKey, o.ApiSecret))
        {
            Api = { Secure = true }
        };
    }

    public async Task<FileUploadResult> UploadProfileImageAsync(
        string userId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (_cloudinary is null)
        {
            return FileUploadResult.Failure("Cloudinary is not configured.");
        }

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = "annualleave/users",
            PublicId = $"user-{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = true
        };

        var result = await _cloudinary.UploadAsync(uploadParams);
        if (result.Error is not null || result.SecureUrl is null)
        {
            return FileUploadResult.Failure(result.Error?.Message ?? "Failed to upload image.");
        }

        return FileUploadResult.Success(result.SecureUrl.ToString(), fileName);
    }

    public async Task<FileUploadResult> UploadEvidenceAsync(
        string userId,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (_cloudinary is null)
        {
            return FileUploadResult.Failure("Cloudinary is not configured.");
        }

        var publicId = $"leave-evidence-{userId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var imageUpload = new ImageUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = "annualleave/evidence",
                PublicId = publicId,
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var imageResult = await _cloudinary.UploadAsync(imageUpload);
            if (imageResult.Error is not null || imageResult.SecureUrl is null)
            {
                return FileUploadResult.Failure(imageResult.Error?.Message ?? "Failed to upload evidence.");
            }

            return FileUploadResult.Success(imageResult.SecureUrl.ToString(), fileName);
        }

        var rawUpload = new RawUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = "annualleave/evidence",
            PublicId = publicId,
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false
        };

        var rawResult = await _cloudinary.UploadAsync(rawUpload);
        if (rawResult.Error is not null || rawResult.SecureUrl is null)
        {
            return FileUploadResult.Failure(rawResult.Error?.Message ?? "Failed to upload evidence.");
        }

        return FileUploadResult.Success(rawResult.SecureUrl.ToString(), fileName);
    }
}
