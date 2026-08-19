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

    /// The resource exists and the caller is authenticated, but it is not
    /// theirs to act on (another employee's timesheet, say) → 403.
    Forbidden = 2,

    /// The request is understood but cannot proceed, typically because of a
    /// precondition on the caller's own account rather than on any resource
    /// (an employee with no EmployeeProfile trying to check in) → 400.
    ///
    /// Distinct from <c>ValidationFailure</c>, which carries per-field errors:
    /// the client treats those as form feedback and deliberately withholds the
    /// global error notification, so routing a precondition through it would
    /// leave the user with no message at all.
    Invalid = 3,
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

    /// <summary>
    /// The caller is authenticated but has no claim on this resource. Distinct
    /// from <see cref="Failure"/> so that an authorization refusal is not
    /// reported as a missing resource, which would send the caller hunting for
    /// a bad id instead of reading the reason.
    /// </summary>
    public static Result<T> Forbidden(string error) => new()
    {
        IsSuccess = false,
        Error = error,
        ErrorKind = ResultErrorKind.Forbidden
    };

    /// <summary>
    /// The request cannot be carried out as asked. See
    /// <see cref="ResultErrorKind.Invalid"/> for why this is not
    /// <see cref="ValidationFailure"/>.
    /// </summary>
    public static Result<T> Invalid(string error) => new()
    {
        IsSuccess = false,
        Error = error,
        ErrorKind = ResultErrorKind.Invalid
    };

    public static Result<T> ValidationFailure(IDictionary<string, string[]> validationErrors, string error = "One or more validation errors occurred.") => new()
    {
        IsSuccess = false,
        Error = error,
        ValidationErrors = validationErrors
    };
}