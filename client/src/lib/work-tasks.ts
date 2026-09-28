import type { WorkTask, WorkTaskPriority, WorkTaskStatus } from './types'

export type TaskTab = 'assigned' | 'created' | 'all'
/** `open` is To do + In progress, the page's default; `any` is everything. */
export type StatusFilter = 'open' | 'any' | WorkTaskStatus

export const STATUS_LABELS: Record<WorkTaskStatus, string> = {
    ToDo: 'To do',
    InProgress: 'In progress',
    Done: 'Done',
    Cancelled: 'Cancelled',
}

export const PRIORITY_LABELS: Record<WorkTaskPriority, string> = { Low: 'Low', Normal: 'Normal', High: 'High' }

export function isOpenTask(task: WorkTask): boolean {
    return task.status === 'ToDo' || task.status === 'InProgress'
}

/**
 * Today as the viewer's local calendar date. Built from the local parts, never
 * `toISOString()`, which reads local midnight back as yesterday east of Greenwich.
 */
export function todayIso(now: Date = new Date()): string {
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Due dates are `YYYY-MM-DD`, so a string comparison is a date comparison. */
export function isOverdue(task: WorkTask, today: string): boolean {
    return isOpenTask(task) && task.dueDate != null && task.dueDate.slice(0, 10) < today
}

function inTab(task: WorkTask, tab: TaskTab, userId: string): boolean {
    if (tab === 'assigned') return task.assignees.some((a) => a.userId === userId)
    if (tab === 'created') return task.createdById === userId
    return true
}

function matchesStatus(task: WorkTask, status: StatusFilter): boolean {
    if (status === 'any') return true
    if (status === 'open') return isOpenTask(task)
    return task.status === status
}

export function filterTasks(
    tasks: readonly WorkTask[],
    filter: { tab: TaskTab; status: StatusFilter; departmentId: number | null; userId: string },
): WorkTask[] {
    return tasks.filter(
        (task) =>
            inTab(task, filter.tab, filter.userId) &&
            matchesStatus(task, filter.status) &&
            (filter.departmentId == null || task.departmentId === filter.departmentId),
    )
}

export function openCount(tasks: readonly WorkTask[], tab: TaskTab, userId: string): number {
    return tasks.filter((task) => inTab(task, tab, userId) && isOpenTask(task)).length
}
