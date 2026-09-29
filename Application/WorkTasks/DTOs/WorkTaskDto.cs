using Domain;

namespace Application.WorkTasks.DTOs;

public class WorkTaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    /// <summary>
    /// Null on a task filed before projects were required, or saved while the Task
    /// Settings' Project field is Optional (<see cref="Application.WorkTasks.Support.WorkTaskFieldRules"/>).
    /// </summary>
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    /// <summary>The project's code and colour, for the card's badge.</summary>
    public string? ProjectCode { get; set; }
    public string? ProjectColorKey { get; set; }
    /// <summary>Everyone on the task, by name.</summary>
    public List<WorkTaskAssigneeDto> Assignees { get; set; } = [];
    public string CreatedById { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public int? TargetHours { get; set; }
    /// <summary>
    /// Hours on every timesheet row linked to the task, whatever the sheet's status.
    /// Summed on every read, never stored. May pass <see cref="TargetHours"/>.
    /// </summary>
    public decimal LoggedHours { get; set; }
    /// <summary>
    /// Null on a task filed before the question was asked, or saved while the Task
    /// Settings hide Billable (<see cref="Application.WorkTasks.Support.WorkTaskFieldRules"/>).
    /// </summary>
    public bool? IsBillable { get; set; }
    public WorkTaskPriority Priority { get; set; }
    public WorkTaskStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>The caller created it, or its creator is deactivated: may edit, reassign and delete.</summary>
    public bool CanEdit { get; set; }

    /// <summary>The caller may edit it, or is one of its assignees: may move its status.</summary>
    public bool CanChangeStatus { get; set; }

    /// <summary>The task waits for confirmation and the caller may give it: see WorkTaskReviewRule.IsReviewer.</summary>
    public bool CanConfirm { get; set; }

    /// <summary>Who confirmed it; null unless Done.</summary>
    public string? ConfirmedByName { get; set; }

    /// <summary>The last send-back, kept until the task is confirmed.</summary>
    public string? SentBackReason { get; set; }
    public DateTime? SentBackAtUtc { get; set; }

    /// <summary>Files attached to explain the work, oldest first. Whoever may edit the task adds them; anyone who sees it opens them.</summary>
    public List<WorkTaskAttachmentDto> Attachments { get; set; } = [];
}

public class WorkTaskAttachmentDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>The relative <c>/api/files/{id}</c> path the file is served from.</summary>
    public string Url { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>The caller attached it, or may edit the task.</summary>
    public bool CanRemove { get; set; }
}

public class WorkTaskAssigneeDto
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// On a task's assignee list only: this person is an HR Administrator, who is never
    /// assignable, left on a task from before the rule. The edit dialog drops them, since
    /// a save that keeps them is refused. Always false on the picker's list.
    /// </summary>
    public bool IsHrAdministrator { get; set; }
}

/// <summary>Somebody in the caller's departments with no task In Progress (<c>GetIdleTaskPeople</c>).</summary>
public class WorkTaskIdlePersonDto
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsManager { get; set; }
    /// <summary>The caller's departments this person covers, in step with <see cref="DepartmentNames"/>.</summary>
    public List<int> DepartmentIds { get; set; } = [];
    public List<string> DepartmentNames { get; set; } = [];
    /// <summary>Tasks they are on that have not started. 0 means no open task at all.</summary>
    public int ToDoCount { get; set; }
}

public class WorkTaskProjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class WorkTaskDepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>A full replace: every field is written as sent.</summary>
public class UpsertWorkTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DepartmentId { get; set; }
    /// <summary>
    /// Whether this is required is a Task Setting, enforced by
    /// <see cref="Application.WorkTasks.Support.WorkTaskFieldRules"/> in the handler, not the
    /// validator; nullable so a missing one reaches that check as a message, not a 0.
    /// </summary>
    public int? ProjectId { get; set; }
    /// <summary>At least one, no repeats — see <c>UpsertWorkTaskRequestValidator</c>.</summary>
    public List<string> AssigneeIds { get; set; } = [];
    public DateOnly? DueDate { get; set; }
    /// <summary>Optional; a null clears it (this is a full replace).</summary>
    public int? TargetHours { get; set; }
    /// <summary>
    /// Whether this is required is a Task Setting, enforced by
    /// <see cref="Application.WorkTasks.Support.WorkTaskFieldRules"/> in the handler, not the
    /// validator; nullable so an unanswered choice reaches that check as a message.
    /// </summary>
    public bool? IsBillable { get; set; }
    public WorkTaskPriority Priority { get; set; } = WorkTaskPriority.Normal;
}

public class UpdateWorkTaskStatusRequest
{
    public WorkTaskStatus Status { get; set; }

    /// <summary>Required when a reviewer sends a waiting task back.</summary>
    public string? Reason { get; set; }
}

/// <summary>One task a timesheet row may be logged against, for the editor's Task picker.</summary>
public class TimesheetTaskOptionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>The picker narrows by it; null only on a task predating required projects.</summary>
    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }
    public int? TargetHours { get; set; }
    public decimal LoggedHours { get; set; }
    /// <summary>Done or cancelled: on the list only because a row on the sheet already names it.</summary>
    public bool IsClosed { get; set; }
    /// <summary>
    /// The sheet's owner is still one of its assignees. False only on a task the sheet
    /// already names, from before they were taken off: kept for that row, pickable nowhere else.
    /// </summary>
    public bool IsAssigned { get; set; }
}
