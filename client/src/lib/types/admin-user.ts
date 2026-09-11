import type { Gender, UserRole } from './user'

/**
 * Re-exported from `./user`, where it moved once it stopped being admin-only
 * data: `UserInfo` carries it too, because the leave picker reads it. Kept
 * exported from here so the admin panels' imports did not all have to move.
 */
export type { Gender }

export interface AdminUser {
    id: string
    userName: string
    email: string
    displayName: string
    imageUrl: string
    phoneNumber?: string | null
    dateOfBirth?: string | null // ISO date "yyyy-MM-dd"
    gender?: Gender | null
    emailConfirmed: boolean
    /**
     * Whether the account may sign in. A leaver is switched off rather than
     * deleted — deleting them nulls out every approval they ever gave. Distinct
     * from presence (which is about being checked in right now) and from the
     * temporary lockout that failed password attempts cause.
     */
    isActive: boolean
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
    /** Null for an Admin, who belongs to no department. */
    departmentId: number | null
    phoneNumber?: string | null
    dateOfBirth?: string | null
    gender?: Gender | null
    managerId?: string | null
    jobTitle?: string | null
    annualLeaveEntitlement?: number
}

/**
 * A full replace, not a patch — the server assigns every field it carries, so
 * a null clears the stored value rather than leaving it alone.
 */
export interface AdminUpdateUserRequest {
    email: string
    displayName: string
    phoneNumber?: string | null
    dateOfBirth?: string | null
    gender?: Gender | null
}

export interface AdminSetUserRolesRequest {
    roles: UserRole[]
}

/**
 * The state to put the account into, rather than a verb, so a repeated or
 * racing call cannot flip it back the other way.
 */
export interface AdminSetUserActiveRequest {
    isActive: boolean
}
