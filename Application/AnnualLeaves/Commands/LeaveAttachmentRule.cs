using Application.Files;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// What a leave's evidence and handover links may point at. Both are stored as
/// the caller sent them, and <c>GetStoredFile</c> grants access to a file by
/// finding it on a leave the caller owns or covers — so an unchecked link was a way
/// to read somebody else's file (a doctor's note, from an id seen once in a
/// manager's view), or to have it mailed to the delegate. Any non-blank text also
/// satisfied a "document required" policy.
///
/// A new link has to be a <c>/api/files/{id}</c> file of the right purpose, uploaded
/// by one of <paramref name="allowedUploaderIds"/> — the filer, or the employee the
/// leave is for. A link the leave already carries passes unchanged, which is how an
/// older request keeps its Cloudinary URL through an edit. One message for every
/// refusal, so a caller learns nothing about files that are not theirs.
/// </summary>
public static class LeaveAttachmentRule
{
    public const string Message = "The attached file could not be found. Upload it again and retry.";

    public static async Task<string?> CheckAsync(
        AppDbContext context, string? url, string? previousUrl, StoredFilePurpose purpose,
        IReadOnlyCollection<string> allowedUploaderIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (string.Equals(url.Trim(), previousUrl?.Trim(), StringComparison.Ordinal))
            return null;

        var id = StoredFilePath.TryParseId(url);
        if (id is null)
            return Message;

        var uploaders = allowedUploaderIds.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var acceptable = await context.StoredFiles.AsNoTracking().AnyAsync(f =>
            f.Id == id && f.Purpose == purpose && uploaders.Contains(f.UploadedById), cancellationToken);
        return acceptable ? null : Message;
    }
}
