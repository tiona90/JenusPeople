// Central registry of all React Query keys. Hooks reference these constants so
// that components, hooks, and the SignalR invalidation block in App.tsx stay in sync.

export const queryKeys = {
    annualLeaves: ['annualLeaves'] as const,
    annualLeaveDetail: (id: string | undefined | null) => ['annualLeaves', 'detail', id] as const,
    teamAwayThisWeekCount: ['teamAwayThisWeekCount'] as const,

    leaveTypes: ['leaveTypes'] as const,
    leaveStatusHistories: ['leaveStatusHistories'] as const,

    departments: ['departments'] as const,
    employeeProfiles: ['employeeProfiles'] as const,
    adminUsers: ['adminUsers'] as const,
    projects: ['projects'] as const,

    timesheets: ['timesheets'] as const,
    myTimesheets: ['timesheets', 'mine'] as const,
    timesheetDetail: (id: string | undefined | null) => ['timesheet', id] as const,
    timesheetStatusHistories: ['timesheetStatusHistories'] as const,

    appSettings: ['appSettings'] as const,

    holidayCountries: ['holidayCountries'] as const,
    holidays: (year: number) => ['holidays', year] as const,

    attendanceToday: ['attendance', 'me', 'today'] as const,
    attendanceHistory: (days: number) => ['attendance', 'history', days] as const,
    teamAttendance: ['attendance', 'team'] as const,
    companyAttendance: ['attendance', 'company'] as const,
} as const
