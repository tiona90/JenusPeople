export interface EmployeeProfile {
    id: string
    userId: string
    displayName: string
    /**
     * Null for an Admin, who sits outside the department structure. Anything
     * grouping profiles by department has to skip these rather than treat the
     * absence as a department of its own.
     */
    departmentId: number | null
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
    departmentId: number | null
}

/**
 * Deliberately carries no leave numbers. `annualLeaveEntitlement` and `leaveBalance`
 * are not edited per person any more — the allowance on Leave Types governs everyone,
 * and the server writes both columns from it (see UpdateLeaveType and CreateAdminUser).
 * Sending them from here is what let a dialog echo a stale figure back and override
 * the allowance for one employee.
 */
export interface EditEmployeeProfileRequest {
    id: string
    /** Null only for an Admin; required for every other role. */
    departmentId: number | null
    managerId: string | null
    jobTitle: string | null
}