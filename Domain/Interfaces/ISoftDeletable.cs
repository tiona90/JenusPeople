namespace Domain.Interfaces;

/// <summary>
/// Entities implementing this are never physically removed from the database.
/// The SaveChangesInterceptor converts a Deleted EF state to a Modified one
/// with IsDeleted = true. Combined with a global HasQueryFilter that excludes
/// IsDeleted rows, callers using context.Remove(...) get soft-delete semantics
/// for free.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
