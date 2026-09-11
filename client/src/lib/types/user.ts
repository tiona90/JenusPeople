export type UserRole = 'Admin' | 'Manager' | 'Employee'

/**
 * Recorded HR data an admin maintains. A string union rather than a number
 * because the API serialises enums by name (`JsonStringEnumConverter`), same as
 * `AttachmentPolicy`. Absent or null means **not specified**, which is not the
 * same as neither: it is the state of every account created before the field
 * existed, and both the leave picker and the server treat it as "offer both
 * parental types" rather than neither. See `lib/parental-leave.ts`.
 */
export type Gender = 'Male' | 'Female'

export interface UserInfo {
    id: string
    userName: string
    email: string
    displayName: string
    imageUrl: string
    phoneNumber?: string | null
    dateOfBirth?: string | null // ISO date "yyyy-MM-dd"
    /**
     * Decides whether Maternity and Paternity Leave are offered on the leave
     * forms. Optional, and null means not specified — both are then offered,
     * subject to the eligible-child half of the rule.
     */
    gender?: Gender | null
    departmentId?: number | null
    departmentName?: string | null
    roles: UserRole[]
    hasChildren?: boolean | null
}
