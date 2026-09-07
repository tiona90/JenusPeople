namespace Application.Files;

/// <summary>Everything the API layer needs to write a stored file onto the wire.</summary>
public class StoredFileDto
{
    public string Id { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Serves as the HTTP ETag, so an unchanged file revalidates cheaply.</summary>
    public string Sha256 { get; set; } = string.Empty;
}
