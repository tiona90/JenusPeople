export type WorkTaskStatus = 'ToDo' | 'InProgress' | 'Done' | 'Cancelled'
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
    /** The plan, quoted only: nothing is measured against it. */
    targetHours: number | null
    targetWeeks: number | null
    priority: WorkTaskPriority
    status: WorkTaskStatus
    createdAtUtc: string
    updatedAtUtc: string
    completedAtUtc: string | null
    /** The caller created it: may edit, reassign, delete. */
    canEdit: boolean
    /** The caller created it or is assigned it: may move its status. */
    canChangeStatus: boolean
}

export interface WorkTaskAssignee { userId: string; displayName: string }
export interface WorkTaskDepartment { id: number; name: string }
export interface WorkTaskProject { id: number; name: string; code: string }

export interface UpsertWorkTaskRequest {
    title: string
    description: string | null
    departmentId: number
    projectId: number
    /** At least one, no repeats. */
    assigneeIds: string[]
    dueDate: string | null
    targetHours: number | null
    targetWeeks: number | null
    priority: WorkTaskPriority
}
