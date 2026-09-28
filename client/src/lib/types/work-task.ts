export type WorkTaskStatus = 'ToDo' | 'InProgress' | 'Done' | 'Cancelled'
export type WorkTaskPriority = 'Low' | 'Normal' | 'High'

/** A to-do one Manager or HR Administrator hands another, scoped to one department. */
export interface WorkTask {
    id: number
    title: string
    description: string | null
    departmentId: number
    departmentName: string
    assigneeId: string
    assigneeName: string
    createdById: string
    createdByName: string
    /** Calendar date, `YYYY-MM-DD`. */
    dueDate: string | null
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

export interface UpsertWorkTaskRequest {
    title: string
    description: string | null
    departmentId: number
    assigneeId: string
    dueDate: string | null
    priority: WorkTaskPriority
}
