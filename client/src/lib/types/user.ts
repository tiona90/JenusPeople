export type UserRole = 'Admin' | 'Manager' | 'Employee'

export interface UserInfo {
    id: string
    userName: string
    email: string
    displayName: string
    imageUrl: string
    phoneNumber?: string | null
    dateOfBirth?: string | null // ISO date "yyyy-MM-dd"
    departmentId?: number | null
    departmentName?: string | null
    roles: UserRole[]
    hasChildren?: boolean | null
}
