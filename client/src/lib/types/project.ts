import type { Department } from './department'

export type ProjectStatus = 'Active' | 'OnHold' | 'Inactive'

export interface ProjectActivity {
    id: number
    name: string
    icon: string
    colorKey: string
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
    departmentId: number | null
    departmentName: string | null
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
}

export interface UpsertProjectRequest {
    id?: number
    name: string
    code: string
    description: string
    isActive: boolean
    status: ProjectStatus
    departmentId: number | null
    ownerId: string | null
    colorKey: string
    targetWeeklyHours: number
    targetMonthlyHours: number
    activityTypeIds: number[]
}
