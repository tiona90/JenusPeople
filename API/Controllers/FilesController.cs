using Application.Files.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace API.Controllers;

/// <summary>
/// Serves uploaded files back out of the database. Profile images and leave
/// evidence used to be fetched straight from Cloudinary's public CDN; routing
/// them through here is what makes access control possible at all.
/// </summary>
[ApiVersion("1.0")]
[Authorize]
public class FilesController : BaseApiController
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetFile(string id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetStoredFile.Query
            {
                Id = id,
                RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            },
            cancellationToken);

        // The handler already collapses "no such file" and "not yours" into the
        // same answer, so there is nothing to distinguish here either.
        if (!result.IsSuccess || result.Value is null)
        {
            return NotFound();
        }

        var file = result.Value;

        // The content hash is a natural strong ETag: the bytes are immutable once
        // stored, so a match means the client's copy is definitely current.
        var etag = $"\"{file.Sha256}\"";
        if (Request.Headers.IfNoneMatch.Any(value => value == etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        // private: these are per-user files, so no shared proxy may keep a copy.
        // must-revalidate with max-age=0 means avatars re-check cheaply (304 on
        // the hash) rather than being re-downloaded, and a replaced image is
        // picked up immediately since its path changes anyway.
        Response.Headers.CacheControl = "private, max-age=0, must-revalidate";

        // inline so an <img> renders and a PDF opens in the browser's viewer; the
        // client's own `download` attribute still forces a save when it wants one.
        // SetHttpFileName handles quoting and non-ASCII names correctly.
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();

        return File(file.Content, file.ContentType);
    }
}
