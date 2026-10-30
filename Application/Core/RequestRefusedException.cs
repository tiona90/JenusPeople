namespace Application.Core;

/// <summary>
/// A request refused for a reason the caller should be told, thrown where a
/// <see cref="Result{T}"/> cannot be returned (the entry actions of
/// <c>TimesheetEntriesController</c>). <c>GlobalExceptionMiddleware</c> answers it
/// with a 400 whose message is <see cref="Exception.Message"/>, so the text has to
/// be fit for the user.
///
/// It exists so the middleware no longer has to guess from framework types: an
/// <see cref="ArgumentException"/>, <see cref="InvalidOperationException"/> or
/// <see cref="KeyNotFoundException"/> is thrown by EF and the BCL for bugs ("The
/// LINQ expression could not be translated", "Sequence contains no elements"), and
/// reading those as the caller's mistake hid them from the System Log and handed
/// their internals back in the response.
/// </summary>
public sealed class RequestRefusedException(string message) : Exception(message);
