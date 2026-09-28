namespace Domain;

/// <summary>Where a task stands. Any move between them is allowed; reopening a Done task is ordinary.</summary>
public enum WorkTaskStatus
{
    ToDo = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3,
}

public enum WorkTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// A to-do one Manager or HR Administrator hands another. Named WorkTask, not
/// Task, so it never collides with System.Threading.Tasks.Task in a handler.
///
/// Scoped by <see cref="DepartmentId"/>: everyone whose ManagerAccessScopeResolver
/// scope covers the department sees it, and nobody else — the creator and the
/// assignee included, once the department leaves their scope. Scope is read on
/// every request, never stored.
/// </summary>
public class WorkTask
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxTargetHours = 9999;

    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>
    /// The project the task is about: an active one assigned to <see cref="DepartmentId"/>.
    /// Required on every save; nullable only for tasks filed before the column,
    /// which must be given one the next time they are edited.
    /// </summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Who is working on it: at least one active Manager or HR Administrator
    /// covering <see cref="DepartmentId"/>. They share one <see cref="Status"/> —
    /// any of them moving it moves it for all.
    /// </summary>
    public ICollection<WorkTaskAssignee> Assignees { get; set; } = new List<WorkTaskAssignee>();

    public string CreatedById { get; set; } = string.Empty;
    public User? CreatedBy { get; set; }

    /// <summary>A calendar date, no zone.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// The plan: how many hours of work it should take. Optional and quoted only —
    /// timesheet entries are not linked to tasks, so nothing is measured against it.
    /// How long it may take is the due date's job.
    /// </summary>
    public int? TargetHours { get; set; }

    /// <summary>
    /// Whether the work is charged to the client. Required on every save, as an
    /// explicit answer rather than an unticked box; nullable only for tasks filed
    /// before the column, which must be given one the next time they are edited.
    /// </summary>
    public bool? IsBillable { get; set; }

    public WorkTaskPriority Priority { get; set; } = WorkTaskPriority.Normal;
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.ToDo;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set on entering Done, cleared on leaving it.</summary>
    public DateTime? CompletedAtUtc { get; set; }
}
