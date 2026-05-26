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

            return NotFound(new ApiErrorResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message = string.IsNullOrWhiteSpace(result.Error) ? "Resource not found." : result.Error,
                Path = HttpContext.Request.Path.Value ?? string.Empty,
                TraceId = HttpContext.TraceIdentifier,
                Timestamp = DateTime.UtcNow
            });
        }

    }
}
