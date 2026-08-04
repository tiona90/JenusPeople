namespace Application.Core;

/// <summary>
/// Why a <see cref="Result{T}"/> failed, so the API layer can pick an accurate
/// status code. <see cref="NotFound"/> is the default because it is what
/// <c>Failure</c> has always mapped to — a handler must opt in to anything else.
/// </summary>
public enum ResultErrorKind
{
    /// The requested resource does not exist → 404.
    NotFound = 0,

    /// The resource exists, but the operation conflicts with its current state
    /// (e.g. deleting a department that still has people in it) → 409.
    Conflict = 1,
}

public class Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string Error { get; init; } = string.Empty;
    public ResultErrorKind ErrorKind { get; init; } = ResultErrorKind.NotFound;
    public IDictionary<string, string[]>? ValidationErrors { get; init; }

    public static Result<T> Success(T value) => new()
    {
        IsSuccess = true,
        Value = value
    };

    public static Result<T> Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };

    /// <summary>
    /// The resource was found, but cannot be operated on in its current state.
    /// Distinct from <see cref="Failure"/> so that a business-rule refusal is
    /// not reported to the client as a missing resource.
    /// </summary>
    public static Result<T> Conflict(string error) => new()
    {
        IsSuccess = false,
        Error = error,
        ErrorKind = ResultErrorKind.Conflict
    };

    public static Result<T> ValidationFailure(IDictionary<string, string[]> validationErrors, string error = "One or more validation errors occurred.") => new()
    {
        IsSuccess = false,
        Error = error,
        ValidationErrors = validationErrors
    };
}