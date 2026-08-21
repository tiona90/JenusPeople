import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AnnualLeave, EmployeeProfile, UserInfo } from '../../lib/types'
import AllLeaveAdminPage from './AllLeaveAdminPage'

// The page states "how many requests are pending" in four places at once: the
// Awaiting Review stat card, the Pending tab badge, the department rollup, and the
// list header. They used to disagree, because the list was date-filtered while the
// counts were not, and because the rollup dropped requests whose owner has no
// EmployeeProfile. Every number has to describe the rows actually on screen.
vi.mock('../../lib/api', () => ({
    getAnnualLeaves: vi.fn(),
    getAppSettings: vi.fn(),
    getDepartments: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getHolidays: vi.fn(),
    getLeaveStatusHistories: vi.fn(),
    getLeaveTypes: vi.fn(),
    updateLeaveStatus: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

const ENGINEERING = { id: 1, name: 'Engineering', code: 'ENG', isActive: true, createdAt: '2026-01-01T00:00:00' }
const FINANCE = { id: 2, name: 'Finance', code: 'FIN', isActive: true, createdAt: '2026-01-01T00:00:00' }

const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', requiresApproval: true, isActive: true, affectsBalance: true,
    icon: '', colorKey: 'primary', description: '', paid: true, attachmentPolicy: 'None',
    defaultAllowance: 25, allowanceUnit: 'days/year', accrualNotes: '', minNoticeDays: 0,
    maxConsecutiveDays: 0, halfDayAllowed: false, eligibilityNotes: '', eligibilityScope: 'All',
} as const

/** A second budget, deliberately smaller than the annual one. */
const SICK_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 2, name: 'Sick Leave', colorKey: 'sick', defaultAllowance: 10,
    // As seeded: sick leave is not deducted from the pooled annual balance, but it does
    // have an allowance of its own.
    affectsBalance: false,
} as const

/** The app-wide fallback entitlement — only used by types that set no allowance. */
const APP_SETTINGS = { defaultAnnualEntitlement: 20, maxCarryoverDays: 5, leaveYearStartMonth: 1 }

/** An ISO date inside the current calendar year, so "used this year" is deterministic. */
function sameYear(month: number, day: number) {
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${new Date().getFullYear()}-${pad(month)}-${pad(day)}T00:00:00`
}

/** An ISO date `months` from the start of the current month, on `day`. */
function monthOffset(months: number, day: number) {
    const now = new Date()
    const d = new Date(now.getFullYear(), now.getMonth() + months, day)
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T00:00:00`
}

function leave(over: Partial<AnnualLeave> & Pick<AnnualLeave, 'id' | 'employeeId' | 'employeeName' | 'startDate' | 'endDate'>): AnnualLeave {
    return {
        leaveTypeId: 1,
        reason: 'Time off.',
        evidenceUrl: null,
        delegateId: null,
        delegateName: '',
        status: 'Pending',
        // Requested well in advance, so nothing counts as urgent.
        createdAt: monthOffset(-1, 2),
        approvedAt: null,
        totalDays: 3,
        departmentName: 'Finance',
        ...over,
    }
}

function profile(over: Partial<EmployeeProfile> & Pick<EmployeeProfile, 'id' | 'userId' | 'displayName' | 'departmentId'>): EmployeeProfile {
    return {
        managerId: null,
        annualLeaveEntitlement: 20,
        leaveBalance: 20,
        jobTitle: null,
        createdAt: '2026-01-01T00:00:00',
        ...over,
    }
}

/* Four pending requests. Two belong to Finance employees with a profile; two belong
   to admin@ and manager1@, who have no EmployeeProfile row at all. Only one of the
   four starts inside the current month. */
const PENDING = [
    leave({
        id: 'p1', employeeId: 'emp-2b', employeeName: 'Employee 2B',
        startDate: monthOffset(0, 5), endDate: monthOffset(0, 7),
    }),
    leave({
        id: 'p2', employeeId: 'emp-2a', employeeName: 'Employee 2A',
        startDate: monthOffset(1, 7), endDate: monthOffset(1, 9),
    }),
    leave({
        id: 'p3', employeeId: 'admin-1', employeeName: 'Admin User', departmentName: '',
        startDate: monthOffset(1, 14), endDate: monthOffset(1, 16),
    }),
    leave({
        id: 'p4', employeeId: 'manager-1', employeeName: 'Manager One', departmentName: '',
        startDate: monthOffset(2, 5), endDate: monthOffset(2, 7),
    }),
]

const DECIDED = [
    leave({
        id: 'd1', employeeId: 'emp-2a', employeeName: 'Employee 2A', status: 'Approved',
        startDate: monthOffset(-4, 2), endDate: monthOffset(-4, 6), totalDays: 5,
        approvedAt: monthOffset(-5, 1),
    }),
    leave({
        id: 'd2', employeeId: 'emp-1a', employeeName: 'Employee 1A', status: 'Rejected',
        departmentName: 'Engineering',
        startDate: monthOffset(-3, 9), endDate: monthOffset(-3, 10), totalDays: 2,
    }),
]

const PROFILES = [
    profile({ id: 'pr1', userId: 'emp-2a', displayName: 'Employee 2A', departmentId: FINANCE.id }),
    profile({ id: 'pr2', userId: 'emp-2b', displayName: 'Employee 2B', departmentId: FINANCE.id }),
    profile({ id: 'pr3', userId: 'emp-1a', displayName: 'Employee 1A', departmentId: ENGINEERING.id }),
]

const ADMIN: UserInfo = {
    id: 'admin-1', userName: 'admin@annualleave.com', email: 'admin@annualleave.com',
    displayName: 'Admin User', imageUrl: '', roles: ['Admin'],
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getAnnualLeaves.mockResolvedValue([...PENDING, ...DECIDED])
    api.getEmployeeProfiles.mockResolvedValue(PROFILES)
    api.getDepartments.mockResolvedValue([ENGINEERING, FINANCE])
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, SICK_LEAVE_TYPE] as never)
    api.getAppSettings.mockResolvedValue(APP_SETTINGS as never)
    api.getLeaveStatusHistories.mockResolvedValue([])
    api.getHolidays.mockResolvedValue([])
})

async function renderPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <QueryClientProvider client={queryClient}>
            <AllLeaveAdminPage user={ADMIN} />
        </QueryClientProvider>,
    )
    await screen.findByText('Leave by Department')
    return view
}

/** The value shown on a stat card, read from the card's own subtree. */
function statCardValue(label: string) {
    // "⏳ Awaiting Review" is also the pending section's heading, so match on the shape
    // of a stat card: a label, a numeric value and a caption.
    const cards = screen.getAllByText(label)
        .map((el) => el.parentElement!)
        .filter((card) => card.children.length === 3 && /^\d+$/.test(card.children[1].textContent ?? ''))
    expect(cards).toHaveLength(1)
    return cards[0].children[1].textContent
}

/** The "Leave by Department" panel element. */
function rollupPanel() {
    const panel = screen.getByText('Leave by Department').parentElement!.parentElement!
    expect(panel.textContent).toContain('YTD')
    return panel
}

/** Every non-zero pending count in the department rollup, keyed by department. */
function rollupPending() {
    const panel = rollupPanel()
    const rows = Array.from(panel.querySelectorAll('strong'))
    return rows.map((strong) => {
        const row = strong.closest('div')!.parentElement!
        return { dept: row.children[0].firstElementChild!.textContent, pending: Number(strong.textContent) }
    })
}

/** How many pending requests are actually rendered — pending rows are the ones with a checkbox. */
function renderedPendingRows() {
    return screen.queryAllByRole('checkbox').length
}

function setDateRange(value: string) {
    const dateSelect = screen.getAllByRole('combobox').find((select) =>
        Array.from(select.querySelectorAll('option')).some((o) => (o as HTMLOptionElement).value === 'this-month'))!
    fireEvent.change(dateSelect, { target: { value } })
}

describe('AllLeaveAdminPage — pending counts agree with the visible rows', () => {
    it('shows every pending request by default', async () => {
        await renderPage()

        expect(renderedPendingRows()).toBe(4)
        expect(statCardValue('⏳ Awaiting Review')).toBe('4')
        expect(screen.getByRole('button', { name: 'Pending 4' })).toBeInTheDocument()
        expect(screen.getByText('· 4 requests')).toBeInTheDocument()
    })

    it('counts requests whose owner has no employee profile in the department rollup', async () => {
        await renderPage()

        const rollup = rollupPending()
        expect(rollup).toEqual([
            { dept: 'Finance', pending: 2 },
            { dept: 'No department', pending: 2 },
        ])
        expect(rollup.reduce((sum, r) => sum + r.pending, 0)).toBe(renderedPendingRows())

        // Engineering has people but nothing pending, and says so rather than being dropped.
        expect(within(rollupPanel()).getByText('✓ None pending')).toBeInTheDocument()
    })

    it('keeps the counts and the list in step when the date range is narrowed', async () => {
        await renderPage()

        setDateRange('this-month')

        await waitFor(() => expect(renderedPendingRows()).toBe(1))
        expect(statCardValue('⏳ Awaiting Review')).toBe('1')
        expect(screen.getByRole('button', { name: 'Pending 1' })).toBeInTheDocument()
        expect(screen.getByText('· 1 request')).toBeInTheDocument()
        expect(rollupPending()).toEqual([{ dept: 'Finance', pending: 1 }])
    })

    it('lets the rollup filter down to the requests with no department', async () => {
        await renderPage()

        // Scoped to the panel: "No department" is also an option in the department filter.
        const noDepartmentRow = within(rollupPanel()).getByText('No department').parentElement!.parentElement!
        fireEvent.click(within(noDepartmentRow).getByRole('button', { name: 'Filter' }))

        await waitFor(() => expect(renderedPendingRows()).toBe(2))
        expect(statCardValue('⏳ Awaiting Review')).toBe('2')
        expect(screen.getByRole('button', { name: 'Pending 2' })).toBeInTheDocument()
        expect(screen.getByText('Admin User')).toBeInTheDocument()
        expect(screen.getByText('Manager One')).toBeInTheDocument()
    })

    // The queue is the only place leave gets approved, so it must not start below a
    // month-tall calendar that had two booked cells in it.
    it('renders the review queue ahead of the calendar', async () => {
        await renderPage()

        const firstRow = screen.getAllByRole('checkbox')[0]
        const calendar = screen.getByText(/· Leave Calendar$/)

        expect(firstRow.compareDocumentPosition(calendar) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
        expect(screen.getByText('Click a request in the list above to see details')).toBeInTheDocument()
    })

    it('counts a request needing attention once, not once per reason', async () => {
        // One request that is both urgent (filed inside 24h of its start) and in conflict
        // with an overlapping request from the same department.
        const start = monthOffset(1, 20)
        const end = monthOffset(1, 21)
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'u1', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                startDate: start, endDate: end, createdAt: start, totalDays: 2,
            }),
            leave({
                id: 'u2', employeeId: 'emp-2b', employeeName: 'Employee 2B',
                startDate: start, endDate: end, totalDays: 2,
            }),
        ])
        await renderPage()

        expect(statCardValue('⚠️ Need Attention')).toBe('2')
        expect(screen.getByText('1 urgent · 2 conflicts')).toBeInTheDocument()
    })
})

/* An allowance belongs to a leave type: sick leave is 10 days a year, annual leave 25
   (or whatever the employee's own entitlement says). The row used to quote one pooled
   annual figure — the employee's profile entitlement — against every request, so a
   sick day was measured against the annual budget and counted towards it. */
describe('AllLeaveAdminPage — a request is measured against its own leave type', () => {
    /** "N left after" for the only rendered row. */
    function balanceLine() {
        return screen.getByText(/left after$/).textContent
    }

    /** "N left after" for every rendered row. */
    function balanceLines() {
        return screen.getAllByText(/left after$/).map((el) => el.textContent)
    }

    it('measures a sick request against the sick allowance', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 's1', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                leaveTypeId: SICK_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        // 10-day sick allowance, none used, 3 requested — not the 20-day annual pool.
        expect(screen.getByText('0/10 used')).toBeInTheDocument()
        expect(balanceLine()).toBe('7 left after')
        expect(screen.getByText('Sick Leave allowance')).toBeInTheDocument()
        expect(screen.queryByText('0/20 used')).not.toBeInTheDocument()
    })

    it('does not let one type\'s usage eat another type\'s allowance', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            // Four sick days already approved this year.
            leave({
                id: 's2', employeeId: 'emp-2a', employeeName: 'Employee 2A', status: 'Approved',
                leaveTypeId: SICK_LEAVE_TYPE.id,
                startDate: sameYear(3, 2), endDate: sameYear(3, 5), totalDays: 4,
            }),
            // A pending annual request from the same person.
            leave({
                id: 'a1', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                leaveTypeId: ANNUAL_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        // The annual row opens on the employee's own entitlement (20), untouched by the
        // sick days: 0 of 20 used, 17 left once these 3 are approved. The decided sick
        // row alongside it counts those 4 days against the 10-day sick allowance.
        expect(screen.getByText('0/20 used')).toBeInTheDocument()
        expect(screen.getByText('4/10 used')).toBeInTheDocument()
        expect(balanceLines()).toEqual(expect.arrayContaining(['17 left after', '6 left after']))
        expect(screen.getByText('Annual Leave allowance')).toBeInTheDocument()
        expect(screen.getByText('Sick Leave allowance')).toBeInTheDocument()
    })

    it('falls back to the leave type when the employee has no entitlement on record', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'a2', employeeId: 'admin-1', employeeName: 'Admin User', departmentName: '',
                leaveTypeId: ANNUAL_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        // admin@ has no EmployeeProfile, so the figure comes from Leave Types (25).
        expect(screen.getByText('0/25 used')).toBeInTheDocument()
        expect(balanceLine()).toBe('22 left after')
    })

    it('says so rather than inventing an allowance for a type that has none', async () => {
        api.getLeaveTypes.mockResolvedValue([
            ANNUAL_LEAVE_TYPE,
            { ...ANNUAL_LEAVE_TYPE, id: 3, name: 'Study Leave', defaultAllowance: 0 },
        ] as never)
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'u1', employeeId: 'emp-2a', employeeName: 'Employee 2A', leaveTypeId: 3,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        // No allowance on the type and no fallback configured either.
        api.getAppSettings.mockResolvedValue({ ...APP_SETTINGS, defaultAnnualEntitlement: 0 } as never)
        await renderPage()

        expect(screen.getByText('—')).toBeInTheDocument()
        expect(screen.queryByText(/left after/)).not.toBeInTheDocument()
    })

    /* The employee's own entitlement (20) is smaller than what Leave Types says for
       annual leave (25). The row quotes the entitlement, so it has to say where the
       difference comes from — otherwise the two pages read as contradicting each other. */
    it('says on hover when an entitlement overrides the leave type', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'a3', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                leaveTypeId: ANNUAL_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        const cell = screen.getByText('0/20 used').parentElement!
        expect(cell.getAttribute('title')).toBe(
            'Annual Leave: 0 of 20 days/year used this year · '
            + 'Leave Types says 25 days/year — overridden for this employee')
    })

    it('marks a type that is tracked outside the annual balance', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 's3', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                leaveTypeId: SICK_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        const cell = screen.getByText('0/10 used').parentElement!
        expect(cell.getAttribute('title')).toBe(
            'Sick Leave: 0 of 10 days/year used this year · '
            + 'Tracked separately — not deducted from the annual balance')
    })
})
