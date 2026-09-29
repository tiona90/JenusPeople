import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AnnualLeave, EmployeeProfile, UserInfo } from '../../lib/types'
import DashboardHome from './DashboardHome'

/**
 * An HR Administrator has the administrator's reach over leave and time and none of
 * the configuration, so the dashboard they open on is about that work — the
 * decisions waiting on them, who is away, balances to watch — and not the
 * "Workspace overview" a System Administrator gets. These pin which dashboard each
 * administrator role lands on, and that the HR one reads the whole company.
 */
vi.mock('../../lib/api', () => ({
    approveTimesheet: vi.fn(),
    getAdminUsers: vi.fn(),
    getAnnualLeaves: vi.fn(),
    getAppSettings: vi.fn(),
    getCompanyAttendance: vi.fn(),
    getDepartments: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getIdleTaskPeople: vi.fn(),
    getLeaveTypes: vi.fn(),
    getProjectActivityTypes: vi.fn(),
    getProjectComponents: vi.fn(),
    getProjects: vi.fn(),
    getProjectTypes: vi.fn(),
    getTimesheets: vi.fn(),
    rejectTimesheet: vi.fn(),
    updateLeaveStatus: vi.fn(),
}))

vi.mock('../../lib/mobx', () => ({ useStore: vi.fn() }))

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', requiresManagerApproval: true, isActive: true, affectsBalance: true,
    icon: '', colorKey: 'primary', description: '', paid: true, attachmentPolicy: 'None',
    defaultAllowance: 23, allowanceUnit: 'days/year', maxCarryoverDays: 0,
    perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0,
    accrualNotes: '', minNoticeDays: 0, maxConsecutiveDays: 0, halfDayAllowed: false, availableTo: 'Both',
}

const SICK_LEAVE_TYPE = { ...ANNUAL_LEAVE_TYPE, id: 2, name: 'Sick Leave', affectsBalance: false, attachmentPolicy: 'Required' }

const HR: UserInfo = {
    id: 'u-hr', userName: 'hr@worktrack.com', email: 'hr@worktrack.com',
    displayName: 'Helen HR', imageUrl: '', roles: ['HR Administrator'], departmentId: null,
}

const SYSTEM_ADMIN: UserInfo = { ...HR, id: 'u-admin', displayName: 'Admin User', roles: ['System Administrator'] }

function iso(daysFromToday: number) {
    const d = new Date(); d.setHours(0, 0, 0, 0); d.setDate(d.getDate() + daysFromToday)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}T00:00:00`
}

function leave(over: Partial<AnnualLeave> & Pick<AnnualLeave, 'id' | 'employeeId' | 'employeeName' | 'status'>): AnnualLeave {
    return {
        startDate: iso(0), endDate: iso(0), leaveTypeId: ANNUAL_LEAVE_TYPE.id, reason: '', evidenceUrl: null,
        delegateId: null, delegateName: '', createdAt: iso(-2), approvedAt: null, totalDays: 1, duration: 0,
        departmentName: 'Engineering', childId: null, childName: '',
        ...over,
    } as AnnualLeave
}

const PROFILES: EmployeeProfile[] = [
    { id: 'pr-hr', userId: HR.id, displayName: HR.displayName, departmentId: null, managerId: null, annualLeaveEntitlement: 23, leaveBalance: 23, jobTitle: null, employmentStartDate: null, createdAt: iso(-400) },
    // Two days left: "Running low".
    { id: 'pr-1', userId: 'u-1', displayName: 'Maria Ioannou', departmentId: 1, managerId: null, annualLeaveEntitlement: 23, leaveBalance: 2, jobTitle: null, employmentStartDate: null, createdAt: iso(-400) },
    { id: 'pr-2', userId: 'u-2', displayName: 'Andreas Georgiou', departmentId: 1, managerId: null, annualLeaveEntitlement: 23, leaveBalance: 15, jobTitle: null, employmentStartDate: null, createdAt: iso(-400) },
]

beforeEach(() => {
    vi.clearAllMocks()
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, SICK_LEAVE_TYPE] as never)
    api.getEmployeeProfiles.mockResolvedValue(PROFILES)
    api.getTimesheets.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([
        { id: 1, name: 'Engineering', code: 'ENG', isActive: true, createdAt: iso(-400) },
        { id: 2, name: 'Finance', code: 'FIN', isActive: true, createdAt: iso(-400) },
    ])
    api.getAdminUsers.mockResolvedValue([
        { id: HR.id, userName: HR.userName, email: HR.email, displayName: HR.displayName, imageUrl: '', emailConfirmed: true, isActive: true, roles: ['HR Administrator'] },
        { id: 'u-1', userName: 'maria@worktrack.com', email: 'maria@worktrack.com', displayName: 'Maria Ioannou', imageUrl: '', emailConfirmed: true, isActive: true, roles: ['Employee'] },
        // Invited, has not set a password yet.
        { id: 'u-2', userName: 'andreas@worktrack.com', email: 'andreas@worktrack.com', displayName: 'Andreas Georgiou', imageUrl: '', emailConfirmed: false, isActive: true, roles: ['Manager'] },
    ] as never)
    api.getProjects.mockResolvedValue([])
    api.getIdleTaskPeople.mockResolvedValue([
        { userId: 'u-1', displayName: 'Maria Ioannou', isManager: false, departmentIds: [1], departmentNames: ['Engineering'], toDoCount: 0 },
    ])
    api.getProjectActivityTypes.mockResolvedValue([])
    api.getProjectComponents.mockResolvedValue([])
    api.getProjectTypes.mockResolvedValue([])
    api.getAppSettings.mockResolvedValue({
        leaveYearStartMonth: 1, emailNotificationsEnabled: true, emailDailyDigest: false, emailUrgentOnly: false,
        reminders: [], holidayCountryName: 'Cyprus', workingDays: 'mon-fri', workingDaysCustom: '',
        workingHoursStart: '09:00', workingHoursEnd: '18:00', weeklyHoursTarget: 40, timeZoneId: 'Asia/Nicosia',
    } as never)
    api.getCompanyAttendance.mockResolvedValue({ total: 2, in: 1, break: 0, out: 0, leave: 1, departments: [], issues: [], recent: [] } as never)
    api.getAnnualLeaves.mockResolvedValue([
        // Away today, in Finance.
        leave({ id: 'l-away', employeeId: 'u-2', employeeName: 'Andreas Georgiou', status: 'Approved', departmentName: 'Finance', startDate: iso(-1), endDate: iso(1), totalDays: 3 }),
        // Waiting on HR's decision — a sick-leave request with no document, which the
        // type requires. With HR, not Pending: a Pending row on a type that asks for
        // the manager is the manager's to decide and stays off this dashboard.
        leave({ id: 'l-pending', employeeId: 'u-1', employeeName: 'Maria Ioannou', status: 'AwaitingHrApproval', leaveTypeId: SICK_LEAVE_TYPE.id, startDate: iso(5), endDate: iso(5) }),
    ])
})

function renderAs(user: UserInfo) {
    mobx.useStore.mockReturnValue({ authStore: { user }, uiStore: {} } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/dashboard']}>
            <QueryClientProvider client={queryClient}>
                <DashboardHome />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}

describe('The HR Administrator dashboard', () => {
    it('is about leave and time, not the workspace', async () => {
        renderAs(HR)

        expect(await screen.findByText('Hi Helen 👋')).toBeInTheDocument()
        expect(screen.getByText("Who's away")).toBeInTheDocument()
        expect(screen.getAllByText('Balances to watch').length).toBeGreaterThan(0)
        expect(screen.getByText('Approval queue')).toBeInTheDocument()
        // Attendance is Leave & Time too, so the feed and the issues sit here now.
        expect(screen.getByText("Today's issues")).toBeInTheDocument()
        expect(screen.getByText('Recent activity')).toBeInTheDocument()
        expect(screen.queryByText('Workspace overview')).not.toBeInTheDocument()
    })

    it('lists who is not working on a task', async () => {
        renderAs(HR)

        const panel = await screen.findByRole('region', { name: 'Not working on a task' })
        expect(panel).toHaveTextContent('Maria Ioannou')
        expect(panel).toHaveTextContent('No tasks')
        expect(screen.getByRole('button', { name: 'Go to Tasks →' })).toBeInTheDocument()
    })

    it('lists who is away across every department, with when they are back', async () => {
        renderAs(HR)
        await screen.findByText("Who's away")

        // Andreas is on approved leave and nowhere else on the page — not pending, and
        // his balance is healthy — so his row is the Who's away card's.
        const row = screen.getByText('Andreas Georgiou').closest('[class]')!.parentElement!.parentElement as HTMLElement
        expect(within(row).getByText('Finance')).toBeInTheDocument()
        expect(within(row).getByText(/^Away · Back/)).toBeInTheDocument()
    })

    it('queues the pending request and holds Approve while its document is missing', async () => {
        renderAs(HR)
        await screen.findByText('Approval queue')

        expect(screen.getByText('Maria Ioannou · Sick Leave')).toBeInTheDocument()
        expect(screen.getByText('📎 Document needed')).toBeInTheDocument()
        const approve = screen.getByRole('button', { name: 'Approve' })
        expect(approve).toBeDisabled()
        expect(approve).toHaveAttribute('title', 'Document needed before approval')
    })

    it('flags the balance that is running low and skips the administrator\'s own profile', async () => {
        renderAs(HR)
        await screen.findAllByText('Balances to watch')

        expect(screen.getByText('Running low')).toBeInTheDocument()
        expect(screen.getByText('Maria Ioannou')).toBeInTheDocument()
        expect(screen.queryByText('Helen HR')).not.toBeInTheDocument()
    })

    it('queues a request that is with HR, with Approve to hand', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({ id: 'l-hr', employeeId: 'u-1', employeeName: 'Maria Ioannou', status: 'AwaitingHrApproval', startDate: iso(5), endDate: iso(5) }),
        ])
        renderAs(HR)
        await screen.findByText('Approval queue')

        expect(screen.getByText('Maria Ioannou · Annual Leave')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled()
    })

    /**
     * The manager stage is the manager's. A Pending request on a type that asks
     * for the manager is not HR's to decide, so it is not queued for them at all —
     * they see it once the manager has approved it.
     */
    /**
     * Timesheets follow the same rule: a submitted sheet a manager is available to
     * review (awaitingManager, from the server) is the manager's; HR's queue holds
     * the ones nobody else can review — a manager's own, a department with no manager.
     */
    it('queues only the timesheets no manager can review', async () => {
        api.getAnnualLeaves.mockResolvedValue([])
        const sheet = (id: string, employeeId: string, employeeName: string, awaitingManager: boolean) => ({
            id, employeeId, employeeName, departmentId: 1, status: 'Submitted', awaitingManager,
            periodStart: iso(-8), periodEnd: iso(-2), totalHours: 40, submittedAt: iso(-1), createdAt: iso(-8), projectSummaries: [],
        })
        api.getTimesheets.mockResolvedValue([
            sheet('t-emp', 'pr-1', 'Maria Ioannou', true),      // the manager's to review
            sheet('t-mgr', 'pr-2', 'Andreas Georgiou', false),  // the manager's own — HR's
        ] as never)
        renderAs(HR)
        await screen.findByText('Approval queue')

        expect(screen.getByText(/Andreas Georgiou · Timesheet/)).toBeInTheDocument()
        expect(screen.queryByText(/Maria Ioannou · Timesheet/)).not.toBeInTheDocument()
        expect(screen.getAllByRole('button', { name: 'Approve' })).toHaveLength(1)
    })

    it("leaves a request that is with the manager off the queue", async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({ id: 'l-mgr', employeeId: 'u-1', employeeName: 'Maria Ioannou', status: 'Pending', startDate: iso(5), endDate: iso(5) }),
        ])
        renderAs(HR)
        await screen.findByText('Approval queue')

        expect(screen.queryByText('Maria Ioannou · Annual Leave')).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    })
})

describe('The System Administrator dashboard', () => {
    it('is about the workspace, with nothing from Leave & Time', async () => {
        renderAs(SYSTEM_ADMIN)

        expect(await screen.findByText('Workspace overview')).toBeInTheDocument()
        expect(screen.getByText('People by department')).toBeInTheDocument()
        expect(screen.getByText('Needs attention')).toBeInTheDocument()
        expect(screen.getByText('System')).toBeInTheDocument()

        expect(screen.queryByText("Who's away")).not.toBeInTheDocument()
        expect(screen.queryByText('Approval queue')).not.toBeInTheDocument()
        expect(screen.queryByText("Today's issues")).not.toBeInTheDocument()
        expect(screen.queryByText('Recent activity')).not.toBeInTheDocument()
        expect(screen.queryByText('Department health')).not.toBeInTheDocument()
    })

    it('points at the invite that has not been accepted and the department with no manager', async () => {
        renderAs(SYSTEM_ADMIN)
        await screen.findByText('Workspace overview')

        expect(screen.getByText('1 invite not yet accepted')).toBeInTheDocument()
        // Engineering's only manager is Andreas; Finance has nobody at all.
        expect(screen.getByText('1 department without a manager')).toBeInTheDocument()
        expect(screen.getByText('Managed by Andreas Georgiou')).toBeInTheDocument()
    })
})
