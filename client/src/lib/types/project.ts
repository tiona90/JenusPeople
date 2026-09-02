import type { Department } from './department'

export type ProjectStatus = 'Active' | 'OnHold' | 'Inactive'

export interface ProjectActivity {
    id: number
    name: string
    icon: string
    colorKey: string
}

// A component as seen from a project. Named apart from the catalogue's own
// `ProjectComponent`, which carries the description and usage count the project
// dialog has no use for.
export interface ProjectComponentSummary {
    id: number
    name: string
    icon: string
    colorKey: string
}

// A type as seen from a project. Named apart from the catalogue's own
// `ProjectType`, which carries the description and usage count the project
// dialog has no use for.
export interface ProjectTypeSummary {
    id: number
    name: string
    icon: string
    colorKey: string
}

export interface ProjectDepartment {
    id: number
    name: string
}

export interface ProjectTeamMember {
    userId: string
    displayName: string
    hoursThisWeek: number
}

export interface Project {
    id: number
    name: string
    code: string
    description: string
    isActive: boolean
    status: ProjectStatus
    // The departments this project belongs to, and therefore who can see it.
    // Only an admin is ever handed a project with none.
    departments: ProjectDepartment[]
    department?: Department | null
    ownerId: string | null
    ownerName: string | null
    colorKey: string
    targetWeeklyHours: number
    targetMonthlyHours: number
    createdAt: string

    hoursThisWeek: number
    hoursThisMonth: number
    hoursYTD: number
    teamSize: number
    team: ProjectTeamMember[]

    // The activity types this project logs time against. Empty means the project
    // has not narrowed the catalogue, and every active activity type applies.
    activities: ProjectActivity[]

    // The components this project is made up of, narrowed from the org-wide
    // catalogue. Empty means the project has declared none.
    components: ProjectComponentSummary[]

    // What kinds of engagement this project is. Empty means it has not been
    // classified, which is a valid state rather than a missing field.
    types: ProjectTypeSummary[]
}

export interface UpsertProjectRequest {
    id?: number
    name: string
    code: string
    description: string
    isActive: boolean
    status: ProjectStatus
    departmentIds: number[]
    ownerId: string | null
    colorKey: string
    targetWeeklyHours: number
    targetMonthlyHours: number
    activityTypeIds: number[]
    componentIds: number[]
    // Empty leaves the project unclassified, which is a valid state rather than
    // a missing field.
    projectTypeIds: number[]
}
