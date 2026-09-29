import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { LeaveStatusHistory, SystemError, UserInfo } from '../../lib/types'
import Topbar from './Topbar'

/**
 * Three bells. A Manager's lists what they can decide, an Employee's lists what
 * happened to their own leave and timesheets, and a System Administrator's lists the
 * errors the system hit. The last used to fall into the employee branch and show
 * everyone's "Leave approved" and "Timesheet rejected" — news about decisions the
 * role neither files nor makes — while the one thing the role is emailed about, a
 * system error, never reached the bell at all.
 */
vi.mock('../../lib/api')
vi.mock('../../lib/mobx')
vi.mock('./AttendanceWidget', () => ({ default: () => null }))

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const SYSTEM_ADMIN: UserInfo = {
    id: 'u-sys',
    userName: 'systemadmin@worktrack.com',
    email: 'systemadmin@worktrack.com',
    displayName: 'Sam System',
    imageUrl: '',
    departmentId: null,
    roles: ['System Administrator'],
}

const EMPLOYEE: UserInfo = {
    ...SYSTEM_ADMIN,
    id: 'u-emp',
    userName: 'maria@worktrack.com',
    email: 'maria@worktrack.com',
    displayName: 'Maria Georgiou',
    departmentId: 2,
    departmentName: 'Finance',
    roles: ['Employee'],
}

const MANAGER: UserInfo = { ...EMPLOYEE, id: 'u-mgr', roles: ['Manager'] }

const recent = new Date(Date.now() - 60 * 60 * 1000).toISOString().replace('Z', '')

const ERRORS: SystemError[] = [
    { id: 7, source: 'GET /api/timesheets', exceptionType: 'System.InvalidCastException', message: 'Specified cast is not valid.', correlationId: 'abc123', occurredAtUtc: recent, lastOccurredAtUtc: recent, occurrences: 3 },
    { id: 8, source: "reminder 'daily-attendance-report'", exceptionType: 'System.IO.IOException', message: 'smtp down', correlationId: null, occurredAtUtc: recent, lastOccurredAtUtc: recent, occurrences: 1 },
]

const HISTORY: LeaveStatusHistory[] = [
    { id: 'h1', annualLeaveId: 'l1', employeeId: 'u-emp', employeeName: 'Maria Georgiou', changedByUserId: 'u-mgr', changedByUserName: 'Mia Manager', leaveTypeName: 'Annual Leave', oldStatus: 'Pending', newStatus: 'Approved', comment: null, changedAt: recent },
]

const navigateToAdminSection = vi.fn()
const navigateToTasks = vi.fn()

function renderTopbarAs(user: UserInfo) {
    mobx.useStore.mockReturnValue({
        authStore: { user, isAuthenticated: true },
        uiStore: { themePreference: 'light', setThemePreference: vi.fn(), navigateToAdminSection, navigateToMyLeave: vi.fn(), navigateToTimesheets: vi.fn(), navigateToTeamLeave: vi.fn(), navigateToTeamTimesheets: vi.fn(), navigateToTasks },
    } as never)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/dashboard']}>
            <QueryClientProvider client={queryClient}>
                <Topbar />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}

beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    api.getSystemErrors.mockResolvedValue(ERRORS)
    api.getLeaveStatusHistories.mockResolvedValue(HISTORY)
    api.getTimesheetStatusHistories.mockResolvedValue([])
    api.getAnnualLeaves.mockResolvedValue([])
    api.getTimesheets.mockResolvedValue([])
    api.getWorkTasks.mockResolvedValue([])
})

describe("The System Administrator's bell", () => {
    it('lists the system errors and not the leave status feed', async () => {
        renderTopbarAs(SYSTEM_ADMIN)

        await waitFor(() => expect(api.getSystemErrors).toHaveBeenCalled())
        // The two unread, recent errors are the badge.
        await waitFor(() => expect(screen.getByText('2')).toBeInTheDocument())

        fireEvent.click(screen.getByRole('button', { name: 'Notifications' }))
        expect(await screen.findByText('System error in GET /api/timesheets (×3)')).toBeInTheDocument()
        expect(screen.getByText(/InvalidCastException/)).toBeInTheDocument()
        expect(screen.getByText("System error in reminder 'daily-attendance-report'")).toBeInTheDocument()

        expect(screen.queryByText(/Leave approved/)).not.toBeInTheDocument()
        // And the status feeds were never even fetched for this role.
        expect(api.getLeaveStatusHistories).not.toHaveBeenCalled()
        expect(api.getTimesheetStatusHistories).not.toHaveBeenCalled()
    })

    it('opens the System Log at the row when an error is clicked, and marks it read', async () => {
        renderTopbarAs(SYSTEM_ADMIN)
        fireEvent.click(screen.getByRole('button', { name: 'Notifications' }))
        fireEvent.click(await screen.findByText('System error in GET /api/timesheets (×3)'))

        expect(navigateToAdminSection).toHaveBeenCalledWith('system-log')
        expect(window.location.hash).toBe('#system-error-7')
        // One read, one still unread.
        await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument())
    })

    it('shows an empty state naming what would appear, not "No notifications yet"', async () => {
        api.getSystemErrors.mockResolvedValue([])
        renderTopbarAs(SYSTEM_ADMIN)
        fireEvent.click(screen.getByRole('button', { name: 'Notifications' }))

        expect(await screen.findByText('No system errors')).toBeInTheDocument()
        expect(screen.queryByText('No notifications yet')).not.toBeInTheDocument()
    })
})

describe("An HR Administrator's bell", () => {
    const HR: UserInfo = { ...SYSTEM_ADMIN, id: 'u-hr', userName: 'hr@worktrack.com', email: 'hr@worktrack.com', displayName: 'Helen HR', roles: ['HR Administrator'] }
    const base = {
        employeeId: 'u-emp', leaveTypeId: 1, reason: '', evidenceUrl: null, delegateId: null, delegateName: '',
        createdAt: recent, approvedAt: null, totalDays: 1, duration: 'Full', departmentName: 'Finance', childId: null, childName: '',
        startDate: '2026-10-05T00:00:00', endDate: '2026-10-05T00:00:00',
    }

    it('lists the requests awaiting HR approval, not the ones with the manager or the status feed', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            { ...base, id: 'l-hr', employeeName: 'Maria Georgiou', status: 'AwaitingHrApproval' },
            { ...base, id: 'l-mgr', employeeName: 'Andreas Georgiou', status: 'Pending' },
        ] as never)
        // Timesheets follow the same rule: the one a manager is available for stays off.
        const sheet = { departmentId: 2, periodStart: '2026-09-14T00:00:00', periodEnd: '2026-09-20T00:00:00', totalHours: 40, submittedAt: recent, createdAt: recent, status: 'Submitted' }
        api.getTimesheets.mockResolvedValue([
            { ...sheet, id: 't-hr', employeeId: 'p-mgr', employeeName: 'Nikos Manager', awaitingManager: false },
            { ...sheet, id: 't-mgr', employeeId: 'p-emp', employeeName: 'Andreas Georgiou', awaitingManager: true },
        ] as never)
        renderTopbarAs(HR)

        await waitFor(() => expect(api.getAnnualLeaves).toHaveBeenCalled())
        // One leave awaiting HR plus one timesheet nobody but HR can review.
        await waitFor(() => expect(screen.getByText('2')).toBeInTheDocument())

        fireEvent.click(screen.getByRole('button', { name: 'Notifications' }))
        expect(await screen.findByText('Leave request from Maria Georgiou awaiting HR approval')).toBeInTheDocument()
        expect(screen.getByText('New timesheet from Nikos Manager')).toBeInTheDocument()
        expect(screen.queryByText(/Andreas Georgiou/)).not.toBeInTheDocument()
        expect(screen.queryByText(/Leave approved/)).not.toBeInTheDocument()
        expect(api.getLeaveStatusHistories).not.toHaveBeenCalled()
        expect(api.getSystemErrors).not.toHaveBeenCalled()
        // The bell polls every 15 seconds and renders no mismatch chip, so it never
        // asks the server for the attendance comparison.
        for (const call of api.getTimesheets.mock.calls) {
            expect((call[0] as { includeAttendance?: boolean } | undefined)?.includeAttendance).not.toBe(true)
        }
    })
})

describe("An employee's bell", () => {
    it('still lists their own status changes and never fetches system errors', async () => {
        renderTopbarAs(EMPLOYEE)

        await waitFor(() => expect(api.getLeaveStatusHistories).toHaveBeenCalled())
        fireEvent.click(screen.getByRole('button', { name: 'Notifications' }))
        expect(await screen.findByText('Leave approved')).toBeInTheDocument()

        expect(api.getSystemErrors).not.toHaveBeenCalled()
        expect(screen.queryByText(/System error/)).not.toBeInTheDocument()
    })

    it("lists a Manager's tasks waiting for their confirmation, and opens Tasks", async () => {
        api.getWorkTasks.mockResolvedValue([
            { id: 5, title: 'Chase notes', status: 'AwaitingConfirmation', canConfirm: true, updatedAtUtc: recent, assignees: [] },
            { id: 6, title: 'Not mine to confirm', status: 'AwaitingConfirmation', canConfirm: false, updatedAtUtc: recent, assignees: [] },
        ] as never)
        renderTopbarAs(MANAGER)

        // The one waiting for them is the badge.
        await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument())
        fireEvent.click(screen.getByRole('button', { name: 'Notifications' }))
        const item = await screen.findByText('Chase notes is waiting for your confirmation')
        expect(screen.queryByText(/Not mine to confirm/)).not.toBeInTheDocument()

        fireEvent.click(item)
        expect(navigateToTasks).toHaveBeenCalled()
    })

    it('does not fetch tasks for an Employee', async () => {
        renderTopbarAs(EMPLOYEE)
        await waitFor(() => expect(api.getLeaveStatusHistories).toHaveBeenCalled())
        expect(api.getWorkTasks).not.toHaveBeenCalled()
    })

    // The task list is heavy (every task in scope, with logged hours); SignalR's
    // notificationsUpdated already refreshes it on every task write.
    it('fetches the tasks once rather than polling them with the rest of the bell', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true })
        try {
            renderTopbarAs(MANAGER)
            await waitFor(() => expect(api.getWorkTasks).toHaveBeenCalledTimes(1))
            const leavePolls = api.getAnnualLeaves.mock.calls.length

            await vi.advanceTimersByTimeAsync(16_000)

            await waitFor(() => expect(api.getAnnualLeaves.mock.calls.length).toBeGreaterThan(leavePolls))
            expect(api.getWorkTasks).toHaveBeenCalledTimes(1)
        } finally {
            vi.useRealTimers()
        }
    })
})
