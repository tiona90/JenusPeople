namespace Domain;

/// <summary>
/// One person on a <see cref="WorkTask"/>. The pair is the key, so nobody is on a
/// task twice. Deleting the task takes these rows with it; the user side is
/// Restrict, so DeleteAdminUser removes a leaver's rows first (and hands a task
/// left with nobody back to its creator).
/// </summary>
public class WorkTaskAssignee
{
    public int WorkTaskId { get; set; }
    public WorkTask? WorkTask { get; set; }

    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
}
