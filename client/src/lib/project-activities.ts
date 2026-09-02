import type { Project, ProjectActivityType } from './types'

/**
 * The activity types a timesheet row may log against.
 *
 * A project narrows the org-wide catalogue to the activities it actually does.
 * A project that has narrowed nothing — which is every project predating
 * project-level assignment — offers the whole catalogue instead of an empty
 * dropdown.
 *
 * `catalogue` is expected to be the active types only; anything the project
 * still references but that has since been disabled drops out with it.
 */
export function activityOptionsFor(
    project: Project | undefined,
    catalogue: ProjectActivityType[],
): ProjectActivityType[] {
    if (!project || project.activities.length === 0) return catalogue

    const assigned = new Set(project.activities.map((a) => a.id))
    return catalogue.filter((a) => assigned.has(a.id))
}

/**
 * The activity a timesheet row keeps after its project changes: the one it has
 * if the new project still offers it, otherwise none.
 *
 * Without this a row could carry an activity the new project has not assigned,
 * which the server refuses on save — clearing it moves that from a failed save
 * to an obviously empty field.
 */
export function retainedActivityId(
    activityTypeId: string,
    project: Project | undefined,
    catalogue: ProjectActivityType[],
): string {
    if (!activityTypeId) return ''

    return activityOptionsFor(project, catalogue).some((a) => String(a.id) === activityTypeId)
        ? activityTypeId
        : ''
}
