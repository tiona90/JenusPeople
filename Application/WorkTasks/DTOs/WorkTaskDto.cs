using Domain;

namespace Application.WorkTasks.DTOs;

public class WorkTaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    /// <summary>Null only on a task filed before projects were required.</summary>
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
    /// <summary>Null only on a task filed before the question was asked.</summary>
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
    /// <summary>Required; nullable so a missing one reaches the validator as a message, not a 0.</summary>
    public int? ProjectId { get; set; }
    /// <summary>At least one, no repeats — see <c>UpsertWorkTaskRequestValidator</c>.</summary>
    public List<string> AssigneeIds { get; set; } = [];
    public DateOnly? DueDate { get; set; }
    /// <summary>Optional; a null clears it (this is a full replace).</summary>
    public int? TargetHours { get; set; }
    /// <summary>Required; nullable so an unanswered choice reaches the validator as a message.</summary>
    public bool? IsBillable { get; set; }
    public WorkTaskPriority Priority { get; set; } = WorkTaskPriority.Normal;
}

public class UpdateWorkTaskStatusRequest
{
    public WorkTaskStatus Status { get; set; }
}
