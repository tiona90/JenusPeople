using System.Security.Cryptography;
using Application.Core;
using Domain;
using MediatR;
using Persistence;

namespace Application.Files.Commands;

/// <summary>
/// Validates an uploaded payload and persists it as a <see cref="StoredFile"/>,
/// returning the new row's id. Every upload in the app funnels through here, so
/// the rules live in one place rather than being repeated per controller as they
/// were while Cloudinary was doing the storing.
/// </summary>
public class StoreFile
{
    public class Command : IRequest<Result<string>>
    {
        public required byte[] Content { get; set; }
        public required string FileName { get; set; }

        /// <summary>What the client claimed the type was. Cross-checked, never trusted.</summary>
        public string? DeclaredContentType { get; set; }

        public required StoredFilePurpose Purpose { get; set; }
        public required string UploadedById { get; set; }
    }

    /// <summary>What each purpose will accept. Anything not listed is refused.</summary>
    private sealed record UploadPolicy(
        FileSignatureValidator.FileKind[] AcceptedKinds,
        int MaxSizeBytes);

    private static readonly Dictionary<StoredFilePurpose, UploadPolicy> Policies = new()
    {
        [StoredFilePurpose.ProfileImage] = new(
            [FileSignatureValidator.FileKind.Jpeg, FileSignatureValidator.FileKind.Png],
            5 * 1024 * 1024),

        [StoredFilePurpose.LeaveEvidence] = new(
            [FileSignatureValidator.FileKind.Jpeg, FileSignatureValidator.FileKind.Png, FileSignatureValidator.FileKind.Pdf],
            10 * 1024 * 1024),
    };

    /// <summary>
    /// Below this, a payload cannot hold a usable image or document — it is a
    /// truncated upload or a probe, not a file.
    /// </summary>
    private const int MinSizeBytes = 100;

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<string>>
    {
        public async Task<Result<string>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!Policies.TryGetValue(request.Purpose, out var policy))
            {
                return Result<string>.Invalid("Unsupported upload purpose.");
            }

            // The caller's file name is untrusted. Reduce it to a bare leaf before
            // it can contribute any directory structure to anything downstream —
            // a Content-Disposition header, or a future export to disk.
            var fileName = SanitiseFileName(request.FileName);
            if (fileName is null)
            {
                return Result<string>.Invalid("That file name cannot be used.");
            }

            if (request.Content.Length < MinSizeBytes)
            {
                return Result<string>.Invalid("That file is too small to be valid.");
            }

            if (request.Content.Length > policy.MaxSizeBytes)
            {
                return Result<string>.Invalid(
                    $"That file is larger than the {policy.MaxSizeBytes / 1024 / 1024}MB limit.");
            }

            var detected = FileSignatureValidator.Detect(request.Content);
            if (detected is null)
            {
                return Result<string>.Invalid("That file's contents are not a recognised image or PDF.");
            }

            if (!policy.AcceptedKinds.Contains(detected.Value))
            {
                var accepted = string.Join(", ", policy.AcceptedKinds.Select(k => k.ToString().ToUpperInvariant()));
                return Result<string>.Invalid($"Only {accepted} files are accepted here.");
            }

            // Extension has to agree with the bytes. Catches a real PNG saved as
            // .exe, which the signature check alone would happily wave through.
            var extension = Path.GetExtension(fileName);
            if (!FileSignatureValidator.ExtensionsFor(detected.Value)
                    .Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return Result<string>.Invalid($"A {detected.Value.ToString().ToUpperInvariant()} file cannot have a '{extension}' extension.");
            }

            // A declared type that names a *different* known format is a lie worth
            // refusing outright. A vague or absent one is simply ignored.
            var declared = FileSignatureValidator.KindForContentType(request.DeclaredContentType);
            if (declared is not null && declared != detected)
            {
                return Result<string>.Invalid("That file's contents do not match its declared type.");
            }

            var file = new StoredFile
            {
                Content = request.Content,
                FileName = fileName,
                ContentType = FileSignatureValidator.ContentTypeFor(detected.Value),
                Sha256 = Convert.ToBase64String(SHA256.HashData(request.Content)),
                SizeBytes = request.Content.Length,
                Purpose = request.Purpose,
                UploadedById = request.UploadedById,
                CreatedAt = DateTime.UtcNow,
            };

            context.StoredFiles.Add(file);
            await context.SaveChangesAsync(cancellationToken);

            return Result<string>.Success(file.Id);
        }

        /// <summary>
        /// Strips any directory component and rejects the degenerate results
        /// (<c>""</c>, <c>.</c>, <c>..</c>) that <see cref="Path.GetFileName(string)"/>
        /// can still return. Both separators are normalised regardless of host OS,
        /// since a Windows-style <c>..\x</c> is not split by GetFileName on Linux.
        /// </summary>
        private static string? SanitiseFileName(string fileName)
        {
            var leaf = Path.GetFileName(fileName.Replace('\\', '/'));

            return string.IsNullOrWhiteSpace(leaf) || leaf == "." || leaf == ".."
                ? null
                : leaf;
        }
    }
}
