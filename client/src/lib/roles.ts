import type { UserRole } from './types/user'

/**
 * Two questions, mirroring `AppRoles` on the server, and they are not the same
 * question:
 *
 * - **Reach** — who may open the company-wide pages: `isAdministrator`. System Administrator and HR Administrator both do — but only the System Administrator sees every department; an HR Administrator sees the departments assigned to them, and the server scopes their data. The role-scoped rules (no department, gender or start date of their own, no coverage on their own leave) read this one.
 * - **System administration** — who configures the system: `isSystemAdministrator`.
 *   System Administrator alone. Users, Departments, Projects and their catalogues,
 *   Leave Types, Organization, Notification Settings and Data Maintenance read this
 *   one, and so does the `/admin/*` route guard.
 *
 * An HR Administrator is the difference between the two: full reach over leave and
 * time, no hand on the configuration. Pick the helper by which question a gate is
 * really asking, never by role name.
 */
export const ADMINISTRATOR_ROLES: readonly UserRole[] = ['System Administrator', 'HR Administrator']

export const SYSTEM_ADMINISTRATOR_ROLES: readonly UserRole[] = ['System Administrator']

/**
 * Who works in Leave & Time — their own leave and timesheets, or everybody's. The
 * System Administrator is the one role outside it: they configure the workspace
 * and neither file nor decide leave, so the Leave & Time routes and sidebar
 * section are gated on this list (`AppRoles.LeaveAndTimeRoles` on the server).
 */
export const LEAVE_AND_TIME_ROLES: readonly UserRole[] = ['HR Administrator', 'Manager', 'Employee']

export function isAdministratorRole(role: string | null | undefined): role is UserRole {
    return role != null && (ADMINISTRATOR_ROLES as readonly string[]).includes(role)
}

/** Whether any of `roles` is an administrator role (may open the company-wide pages). Unknown roles read as not. */
export function isAdministrator(roles: readonly string[] | null | undefined): boolean {
    return !!roles && roles.some(isAdministratorRole)
}

/** Whether any of `roles` may administer the system (People, Configuration, System). Unknown roles read as not. */
export function isSystemAdministrator(roles: readonly string[] | null | undefined): boolean {
    return !!roles && roles.some((role) => (SYSTEM_ADMINISTRATOR_ROLES as readonly string[]).includes(role))
}

/** Whether any of `roles` is the HR Administrator — the role that gives the final approval on a leave type that asks for HR. */
export function isHrAdministrator(roles: readonly string[] | null | undefined): boolean {
    return !!roles && roles.includes('HR Administrator')
}

/**
 * Who uses Tasks: the two department-scoped roles, mirroring
 * `AppRoles.LeaveAndTimeDecisionRoles` on `WorkTasksController`.
 */
export const TASK_ROLES: readonly UserRole[] = ['HR Administrator', 'Manager']

export function canUseTasks(roles: readonly string[] | null | undefined): boolean {
    return !!roles && roles.some((role) => (TASK_ROLES as readonly string[]).includes(role))
}
