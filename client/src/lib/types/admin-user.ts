import type { UserRole } from './user'

export interface AdminUser {
    id: string
    userName: string
    email: string
    displayName: string
    imageUrl: string
    emailConfirmed: boolean
    roles: UserRole[]
}

export interface AdminCreateUserRequest {
    email: string
    displayName: string
    password: string
    roles: UserRole[]
    departmentId: number
}

export interface AdminUpdateUserRequest {
    email: string
    displayName: string
}

export interface AdminSetUserRolesRequest {
    roles: UserRole[]
}