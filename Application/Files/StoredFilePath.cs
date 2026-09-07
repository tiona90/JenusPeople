namespace Application.Files;

/// <summary>
/// The relative path a stored file is served from. Both sides of the contract
/// need it: uploads write this into <c>User.ImageUrl</c> /
/// <c>AnnualLeave.EvidenceUrl</c>, and evidence authorization finds the owning
/// leave by matching against the stored value — so the shape must be built in
/// exactly one place.
///
/// Kept relative rather than absolute so it stays correct across environments
/// (Vite proxies <c>/api</c> in development; the SPA is served same-origin from
/// wwwroot in production).
/// </summary>
public static class StoredFilePath
{
    public const string Prefix = "/api/files/";

    public static string For(string storedFileId) => Prefix + storedFileId;
}
