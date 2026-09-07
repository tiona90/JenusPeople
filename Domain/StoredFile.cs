namespace Domain;

/// <summary>
/// What a <see cref="StoredFile"/> was uploaded for. Drives both the upload
/// validation rules (accepted formats and size ceiling) and who is allowed to
/// read the bytes back, so a file can never be re-purposed by asking for it
/// through a different endpoint.
/// </summary>
public enum StoredFilePurpose
{
    ProfileImage = 0,
    LeaveEvidence = 1,
}

/// <summary>
/// An uploaded file held in the database. Replaces the previous Cloudinary
/// integration: <c>User.ImageUrl</c> and <c>AnnualLeave.EvidenceUrl</c> now hold
/// a relative <c>/api/files/{id}</c> path pointing at one of these rows instead
/// of an absolute URL on a third-party CDN.
/// </summary>
public class StoredFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public byte[] Content { get; set; } = [];

    /// <summary>Bare leaf name — any directory component the caller sent is stripped on the way in.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The type detected from the leading bytes, never the one the caller
    /// declared. Serving a caller-supplied content type would let an uploader
    /// choose how the browser interprets their bytes.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Base64 SHA-256 of <see cref="Content"/>; doubles as the HTTP ETag.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public StoredFilePurpose Purpose { get; set; }

    public string UploadedById { get; set; } = string.Empty;
    public User? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
