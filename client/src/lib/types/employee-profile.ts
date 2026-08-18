export interface EmployeeProfile {
    id: string
    userId: string
    displayName: string
    departmentId: number
    managerId: string | null
    annualLeaveEntitlement: number
    leaveBalance: number
    jobTitle: string | null
    createdAt: string
}

/** Colleague card returned by /employeeprofiles/teammates — no balance data. */
export interface Teammate {
    userId: string
    displayName: string
    jobTitle: string | null
    departmentId: number
}

export interface EditEmployeeProfileRequest {
    id: string
    departmentId: number
    managerId: string | null
    annualLeaveEntitlement: number
    leaveBalance: number
    jobTitle: string | null
}