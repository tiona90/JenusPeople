using System;

namespace Domain;

public enum AuditAction
{
    Create,
    Update,
    Delete
}

public class AuditLog
{
    public long Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string Changes { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public User? User { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
