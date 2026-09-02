import type { Project, ProjectComponent } from './types'

/**
 * The components a timesheet row may log against.
 *
 * A project declares which components it is made up of, narrowing the org-wide
 * catalogue. A project that has declared none — which is every project
 * predating component assignment — offers the whole catalogue rather than an
 * empty dropdown, the same fallback {@link activityOptionsFor} uses.
 *
 * `catalogue` is expected to be the active components only; anything the project
 * still references but that has since been disabled drops out with it.
 */
export function componentOptionsFor(
    project: Project | undefined,
    catalogue: ProjectComponent[],
): ProjectComponent[] {
    if (!project || project.components.length === 0) return catalogue

    const declared = new Set(project.components.map((c) => c.id))
    return catalogue.filter((c) => declared.has(c.id))
}

/**
 * The component a timesheet row keeps after its project changes: the one it has
 * if the new project is made up of it, otherwise none.
 *
 * Without this a row could carry a component the new project has not declared,
 * which the server refuses on save.
 */
export function retainedComponentId(
    componentId: string,
    project: Project | undefined,
    catalogue: ProjectComponent[],
): string {
    if (!componentId) return ''

    return componentOptionsFor(project, catalogue).some((c) => String(c.id) === componentId)
        ? componentId
        : ''
}
