import type { UserRole } from './user'

export interface AdminUser {
    id: string
    userName: string
    email: string
    displayName: string
    imageUrl: string
    phoneNumber?: string | null
    dateOfBirth?: string | null // ISO date "yyyy-MM-dd"
    emailConfirmed: boolean
    roles: UserRole[]
    /**
     * Present only on the create response: whether the welcome email carrying
     * the set-your-password link actually went out.
     */
    inviteEmailSent?: boolean | null
}

/**
 * No password: an admin never sets one. The account is created without a
 * password and its owner chooses theirs from the welcome email's link.
 */
export interface AdminCreateUserRequest {
    email: string
    displayName: string
    roles: UserRole[]
    departmentId: number
    phoneNumber?: string | null
    dateOfBirth?: string | null
}

export interface AdminUpdateUserRequest {
    email: string
    displayName: string
    phoneNumber?: string | null
    dateOfBirth?: string | null
}

export interface AdminSetUserRolesRequest {
    roles: UserRole[]
}