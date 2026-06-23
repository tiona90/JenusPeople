namespace Application.Core;

/// <summary>
/// Shared messaging for optimistic-concurrency conflicts surfaced when a
/// <c>RowVersion</c>-tracked entity was modified by another request between
/// load and save. Handlers map <c>DbUpdateConcurrencyException</c> to a
/// <see cref="Result{T}"/> Failure carrying this message — no auto-merge.
/// </summary>
public static class ConcurrencyError
{
    public const string Message =
        "This record was changed by someone else since you loaded it. Please refresh and try again.";
}
