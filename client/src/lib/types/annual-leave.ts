export type AnnualLeaveStatus =
    | 'Pending'
    | 'Approved'
    | 'Rejected'
    | 'Cancelled'

export interface AnnualLeaveBase {
    startDate: string
    endDate: string
    leaveTypeId: number
    reason: string
    evidenceUrl?: string | null
    /** User id of the colleague nominated to cover while the employee is away. */
    delegateId?: string | null
}

export interface AnnualLeave {
    id: string
    employeeId: string
    startDate: string
    endDate: string
    leaveTypeId: number | null
    reason: string
    evidenceUrl: string | null
    delegateId: string | null
    delegateName: string
    status: AnnualLeaveStatus
    createdAt: string
    approvedAt: string | null
    totalDays: number
    employeeName: string
    departmentName: string
}

export interface CreateAnnualLeaveRequest extends AnnualLeaveBase {
    employeeId: string
}

export interface EditAnnualLeaveRequest extends AnnualLeaveBase {
    id: string
    status?: AnnualLeaveStatus
    statusComment?: string
}
