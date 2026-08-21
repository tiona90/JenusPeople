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
    defaultAllowance: 20, allowanceUnit: 'days', accrualNotes: '', minNoticeDays: 0,
    maxConsecutiveDays: 0, halfDayAllowed: false, eligibilityNotes: '', eligibilityScope: 'All',
} as const

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
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE] as never)
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
