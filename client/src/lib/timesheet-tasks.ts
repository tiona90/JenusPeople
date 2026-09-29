import type { TimesheetTaskOption } from './types'
import { describeTaskProgress } from './work-tasks'

/*
 * The Task column on a timesheet row. Mirrors TimesheetEntryTaskRule: a row may
 * name one of its owner's open tasks on its own project. The server re-checks
 * only a changed task or project, so a task closed since, or one the owner was
 * taken off, stays listed for the row already naming it, and for no other.
 */

/** Open and still the owner's: what a row may newly pick, and a copy may carry. */
export function isPickable(option: TimesheetTaskOption): boolean {
    return !option.isClosed && option.isAssigned !== false
}

/**
 * The Task picker's list. Task is the row's first pick and decides the project,
 * so it is not narrowed by whatever project the row holds. A legacy task with no
 * project is left out: no row could save it — unless the row already names it,
 * since its own project may have been cleared since (the picker must not open
 * blank for a row already logged against it).
 */
export function taskOptionsForRow(options: TimesheetTaskOption[], currentTaskId: string): TimesheetTaskOption[] {
    return options.filter((o) =>
        String(o.id) === currentTaskId
        || (o.projectId != null && isPickable(o)))
}

/** The project a picked task sets on its row, or null for no task (or one not listed yet). */
export function projectIdForTask(workTaskId: string, options: TimesheetTaskOption[]): string | null {
    const option = options.find((o) => String(o.id) === workTaskId)
    return option?.projectId != null ? String(option.projectId) : null
}

/**
 * The row's task after its project moves: kept if it belongs to the new project,
 * otherwise cleared, since the server would refuse it. An id not in the list yet
 * (options still loading) is left alone rather than wiped.
 */
export function retainedWorkTaskId(workTaskId: string, projectId: string, options: TimesheetTaskOption[]): string {
    if (!workTaskId) return ''
    const option = options.find((o) => String(o.id) === workTaskId)
    if (!option) return workTaskId
    return String(option.projectId) === projectId ? workTaskId : ''
}

/** "Copy to rest of week" carries a task only while it is still pickable. */
export function copyableWorkTaskId(workTaskId: string, options: TimesheetTaskOption[]): string {
    const option = options.find((o) => String(o.id) === workTaskId)
    return option && isPickable(option) ? workTaskId : ''
}

export function taskOptionLabel(option: TimesheetTaskOption): string {
    // The task's own name only: the Project column beside it already says which project.
    const name = option.title
    if (option.isClosed) return `${name} (closed)`
    if (option.isAssigned === false) return `${name} (no longer yours)`
    const { text } = describeTaskProgress(option.targetHours, option.loggedHours)
    // The picker has room for one figure: what is left, how far over, or what is logged.
    const short = text.includes(' · ') ? text.split(' · ')[1] : text
    return `${name} — ${short === 'nothing logged' ? '0h logged' : short}`
}
