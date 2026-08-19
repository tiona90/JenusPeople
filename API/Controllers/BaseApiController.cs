using Application.Core;
using API.Models;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("api/[controller]")] // unversioned alias resolves to the default API version (v1)
    public class BaseApiController : ControllerBase
    {
        private IMediator? _mediator;
        protected IMediator Mediator =>
        _mediator ??= HttpContext.RequestServices.GetService<IMediator>()
        ?? throw new InvalidOperationException("IMediator Service is unvailable");

        protected ActionResult HandleResult<T>(Result<T> result)
        {
            if (result.IsSuccess)
            {
                return result.Value is null ? NotFound() : Ok(result.Value);
            }

            if (result.ValidationErrors is not null && result.ValidationErrors.Count > 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Message = string.IsNullOrWhiteSpace(result.Error)
                        ? "One or more validation errors occurred."
                        : result.Error,
                    Path = HttpContext.Request.Path.Value ?? string.Empty,
                    TraceId = HttpContext.TraceIdentifier,
                    Timestamp = DateTime.UtcNow,
                    Errors = result.ValidationErrors
                });
            }

            // An authorization refusal is not a missing resource. Answering 404
            // here would tell a legitimate owner who mistyped an id the same
            // thing it tells someone reaching for a record that isn't theirs,
            // and neither of them learns why the call failed.
            if (result.ErrorKind == ResultErrorKind.Forbidden)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    Message = string.IsNullOrWhiteSpace(result.Error) ? "You are not authorized to perform this action." : result.Error,
                    Path = HttpContext.Request.Path.Value ?? string.Empty,
                    TraceId = HttpContext.TraceIdentifier,
                    Timestamp = DateTime.UtcNow
                });
            }

            // A business-rule refusal (the resource exists but the operation is
            // not allowed against its current state) must not be reported as a
            // 404 — that is indistinguishable from a missing route and sends
            // people hunting for deployment faults instead of reading the body.
            if (result.ErrorKind == ResultErrorKind.Conflict)
            {
                return Conflict(new ApiErrorResponse
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Message = string.IsNullOrWhiteSpace(result.Error) ? "The request conflicts with the current state." : result.Error,
                    Path = HttpContext.Request.Path.Value ?? string.Empty,
                    TraceId = HttpContext.TraceIdentifier,
                    Timestamp = DateTime.UtcNow
                });
            }

            return NotFound(new ApiErrorResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message = string.IsNullOrWhiteSpace(result.Error) ? "Resource not found." : result.Error,
                Path = HttpContext.Request.Path.Value ?? string.Empty,
                TraceId = HttpContext.TraceIdentifier,
                Timestamp = DateTime.UtcNow
            });
        }

        // Returns a list result as a plain JSON array (backward-compatible body)
        // and, when the result is paged, advertises the page metadata via headers
        // so existing clients that ignore them are unaffected.
        protected ActionResult Paged<T>(PagedResult<T> result)
        {
            if (result.Page.HasValue)
            {
                Response.Headers["X-Total-Count"] = result.Total.ToString();
                Response.Headers["X-Page"] = result.Page.Value.ToString();
                Response.Headers["X-Page-Size"] = (result.PageSize ?? result.Items.Count).ToString();
            }
            return Ok(result.Items);
        }

    }
}
