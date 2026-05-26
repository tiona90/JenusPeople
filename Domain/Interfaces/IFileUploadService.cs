namespace Domain.Interfaces;

public record FileUploadResult(string? Url, string? FileName, string? ErrorMessage)
{
    public bool IsSuccess => Url is not null && ErrorMessage is null;

    public static FileUploadResult Success(string url, string fileName) =>
        new(url, fileName, null);

    public static FileUploadResult Failure(string error) =>
        new(null, null, error);
}

public interface IFileUploadService
{
    Task<FileUploadResult> UploadProfileImageAsync(
        string userId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<FileUploadResult> UploadEvidenceAsync(
        string userId,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
