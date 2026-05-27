using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Domain;
using Domain.Interfaces;

namespace Persistence.Interceptors;

/// <summary>
/// One interceptor handles both concerns because they share a single walk of the
/// change tracker. Order matters: capture the audit snapshot FIRST (so a logical
/// delete is still recorded as a Delete action), THEN convert physical deletes
/// to soft deletes. The audit entries are added to the same SaveChanges call so
/// they land in the same transaction as the underlying writes.
/// </summary>
public class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ICurrentUserAccessor? _currentUserAccessor;

    public AuditingSaveChangesInterceptor(ICurrentUserAccessor? currentUserAccessor = null)
    {
        _currentUserAccessor = currentUserAccessor;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return ValueTask.FromResult(result);

        var auditEntries = CaptureAuditEntries(context);
        ApplySoftDeletes(context);

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var context = eventData.Context;
        if (context is null) return result;

        var auditEntries = CaptureAuditEntries(context);
        ApplySoftDeletes(context);

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }

        return result;
    }

    private static void ApplySoftDeletes(DbContext context)
    {
        // Iterate over a snapshot — entry.State assignment mutates the change tracker.
        var deleted = context.ChangeTracker
            .Entries<ISoftDeletable>()
            .Where(e => e.State == EntityState.Deleted)
            .ToList();

        foreach (var entry in deleted)
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
        }
    }

    private List<AuditLog> CaptureAuditEntries(DbContext context)
    {
        var entries = new List<AuditLog>();
        var userId = _currentUserAccessor?.UserId;
        var timestamp = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Never recursively audit the audit table itself.
            if (entry.Entity is AuditLog) continue;
            if (entry.Entity is not IAuditable) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var (action, changes) = entry.State switch
            {
                EntityState.Added => (AuditAction.Create, SerializeCurrentValues(entry)),
                EntityState.Deleted => (AuditAction.Delete, SerializeOriginalValues(entry)),
                _ => (AuditAction.Update, SerializeDiff(entry)),
            };

            // Modifications where no scalar value actually changed are dropped to
            // avoid noisy "{}" rows. Setting IsDeleted = true via soft delete still
            // changes a real scalar so those are captured as the prior Delete entry.
            if (action == AuditAction.Update && changes == "{}") continue;

            entries.Add(new AuditLog
            {
                EntityName = entry.Entity.GetType().Name,
                EntityId = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString() ?? string.Empty,
                Action = action,
                Changes = changes,
                UserId = string.IsNullOrEmpty(userId) ? null : userId,
                Timestamp = timestamp,
            });
        }

        return entries;
    }

    private static string SerializeCurrentValues(EntityEntry entry)
    {
        var dict = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty())
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        return JsonSerializer.Serialize(dict, AuditJsonOptions);
    }

    private static string SerializeOriginalValues(EntityEntry entry)
    {
        var dict = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty())
            .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
        return JsonSerializer.Serialize(dict, AuditJsonOptions);
    }

    private static string SerializeDiff(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (prop.Metadata.IsShadowProperty()) continue;
            if (!prop.IsModified) continue;
            if (Equals(prop.OriginalValue, prop.CurrentValue)) continue;
            dict[prop.Metadata.Name] = new { From = prop.OriginalValue, To = prop.CurrentValue };
        }
        return JsonSerializer.Serialize(dict, AuditJsonOptions);
    }
}
