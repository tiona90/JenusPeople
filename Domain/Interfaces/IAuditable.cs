namespace Domain.Interfaces;

/// <summary>
/// Marker interface. Any entity implementing this generates an AuditLog row
/// on every insert/update/delete via the SaveChangesInterceptor.
/// </summary>
public interface IAuditable
{
}
