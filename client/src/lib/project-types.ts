import type { Project, ProjectType } from './types'

/**
 * The types a timesheet row may be logged under — the ones at least one visible
 * project is classified as.
 *
 * Filtering by the projects rather than handing over the whole catalogue keeps
 * the picker from offering a dead end: choosing a type no project carries would
 * empty the project dropdown underneath it.
 *
 * `catalogue` is expected to be the active types only.
 */
export function typeOptionsFrom(projects: Project[], catalogue: ProjectType[]): ProjectType[] {
    const carried = new Set(projects.flatMap((p) => p.types.map((t) => t.id)))
    return catalogue.filter((t) => carried.has(t.id))
}

/**
 * The projects a timesheet row may log against, given the type chosen for it.
 *
 * No type chosen means no narrowing — every project, including the unclassified
 * ones, which are otherwise unreachable once a type is picked.
 */
export function projectOptionsFor(projectTypeId: string, projects: Project[]): Project[] {
    if (!projectTypeId) return projects

    return projects.filter((p) => p.types.some((t) => String(t.id) === projectTypeId))
}

/**
 * The project a timesheet row keeps after its type changes: the one it has if
 * that project is classified as the new type, otherwise none.
 *
 * The mirror of {@link retainedActivityId} one level up — without it a row could
 * carry a project the chosen type does not apply to, which the server refuses on
 * save. Clearing it turns a failed save into an obviously empty field.
 */
export function retainedProjectId(projectId: string, projectTypeId: string, projects: Project[]): string {
    if (!projectId) return ''

    return projectOptionsFor(projectTypeId, projects).some((p) => String(p.id) === projectId)
        ? projectId
        : ''
}
