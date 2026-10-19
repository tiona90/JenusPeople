export type WorkTaskStatus = 'ToDo' | 'InProgress' | 'Done' | 'Cancelled' | 'AwaitingConfirmation'
export type WorkTaskPriority = 'Low' | 'Normal' | 'High'

/** A to-do one Manager or HR Administrator hands another, scoped to one department. */
export interface WorkTask {
    id: number
    title: string
    description: string | null
    departmentId: number
    departmentName: string
    /** Null only on a task filed before projects were required; it must be given one on its next edit. */
    projectId: number | null
    projectName: string | null
    /** The project's code and colour key, for the card's badge. */
    projectCode: string | null
    projectColorKey: string | null
    /** Everyone on the task, by name. They share one status. */
    assignees: WorkTaskAssignee[]
    createdById: string
    createdByName: string
    /** Calendar date, `YYYY-MM-DD`. */
    dueDate: string | null
    /** The plan. Measured against loggedHours; going over is allowed. */
    targetHours: number | null
    /** Hours on every timesheet row linked to the task, any sheet status. Missing from an older API. */
    loggedHours: number
    priority: WorkTaskPriority
    status: WorkTaskStatus
    createdAtUtc: string
    updatedAtUtc: string
    completedAtUtc: string | null
    /** The caller created it: may edit, reassign, delete. */
    canEdit: boolean
    /** The caller created it or is assigned it: may move its status. */
    canChangeStatus: boolean
    /** Waiting for confirmation, and the caller may confirm it or send it back. Missing from an older API. */
    canConfirm?: boolean
    /** Who confirmed it; set only on a Done task. */
    confirmedByName?: string | null
    /** The last send-back, kept until the task is confirmed. */
    sentBackReason?: string | null
    sentBackAtUtc?: string | null
    /** Files attached to explain the work, oldest first. Added by whoever may edit the task (canEdit); anyone who sees it opens them. Missing from an older API. */
    attachments?: WorkTaskAttachment[]
}

export interface WorkTaskAttachment {
    id: number
    fileName: string
    contentType: string
    sizeBytes: number
    /** The relative `/api/files/{id}` path; pass it through `resolveFileUrl`. */
    url: string
    uploadedByName: string
    createdAtUtc: string
    /** The caller attached it, or may edit the task. */
    canRemove: boolean
}

export interface WorkTaskAssignee {
    userId: string
    displayName: string
    /** On a task's list only: an HR Administrator left on from before HR stopped being assignable. */
    isHrAdministrator?: boolean
}
export interface WorkTaskDepartment { id: number; name: string }

/** Somebody in the caller's departments with no task In Progress (`GET /worktasks/idle-people`). */
export interface WorkTaskIdlePerson {
    userId: string
    displayName: string
    isManager: boolean
    /** The caller's departments they cover, in step with departmentNames. */
    departmentIds: number[]
    departmentNames: string[]
    /** Tasks they are on that have not started; 0 means no open task at all. */
    toDoCount: number
}
export interface WorkTaskProject { id: number; name: string; code: string }

export interface UpsertWorkTaskRequest {
    title: string
    description: string | null
    departmentId: number
    /** Null when the Task Settings make Project optional and none is chosen. */
    projectId: number | null
    /** At least one, no repeats. */
    assigneeIds: string[]
    dueDate: string | null
    targetHours: number | null
    priority: WorkTaskPriority
}

/** A task a timesheet row may be logged against — the owner's open ones, plus any already on the sheet. */
export interface TimesheetTaskOption {
    id: number
    title: string
    projectId: number | null
    projectCode: string | null
    targetHours: number | null
    loggedHours: number
    /** Done or cancelled: listed only because a row on the sheet already names it. */
    isClosed: boolean
    /** False on a task the sheet names from before its owner was taken off it. Missing from an older API: read as true. */
    isAssigned?: boolean
}

/** Hours an assignee logs on a task from its card (POST /worktasks/{id}/log-time). */
export interface LogTaskTimeRequest {
    /** yyyy-mm-dd, the viewer's calendar date. */
    date: string
    hours: number
    notes: string | null
}

/** Where the hours went: the task as it now reads, and the week they landed in. */
export interface LogTaskTimeResult {
    task: WorkTask
    timesheetId: string
    entryId: string
    /** yyyy-mm-dd, the Monday the week starts on. */
    weekStart: string
    /** The task's hours on that date, after this log. */
    hoursOnEntry: number
    /** Everything on the sheet for that date, after this log. */
    hoursOnDay: number
    timesheetStatus: string
}
