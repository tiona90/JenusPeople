import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AnnualLeave, EmployeeProfile, UserInfo } from '../../lib/types'
import { proRateFirstYearAllowance } from '../../lib/leave-allowance'
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
    id: 1, name: 'Annual Leave', requiresManagerApproval: true, isActive: true, affectsBalance: true,
    icon: '', colorKey: 'primary', description: '', paid: true, attachmentPolicy: 'None',
    defaultAllowance: 25, allowanceUnit: 'days/year', maxCarryoverDays: 0,
    perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0,
    accrualNotes: '', minNoticeDays: 0,
    maxConsecutiveDays: 0, halfDayAllowed: false, availableTo: 'Both',
} as const

/** A second budget, deliberately smaller than the annual one. */
const SICK_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 2, name: 'Sick Leave', colorKey: 'sick', defaultAllowance: 10,
    // As seeded: sick leave is not deducted from the pooled annual balance, but it does
    // have an allowance of its own.
    affectsBalance: false,
} as const

/** Per-child budget: `defaultAllowance` is 0 by migration on purpose — the real
 *  budget lives in `perChildTotalWeeks`/`perChildWeeksPerYear` instead. */
const PATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 3, name: 'Paternity Leave', colorKey: 'paternity',
    defaultAllowance: 0, affectsBalance: false,
    perChildEntitlement: true, perChildTotalWeeks: 18, perChildWeeksPerYear: 5, childEligibleUntilAge: 15,
} as const

/** Leave Settings no longer carries an entitlement; only the carryover cap and year. */
const APP_SETTINGS = { leaveYearStartMonth: 1 }

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
        duration: 'Full',
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
        childId: null,
        childName: '',
        ...over,
    }
}

function profile(over: Partial<EmployeeProfile> & Pick<EmployeeProfile, 'id' | 'userId' | 'displayName' | 'departmentId'>): EmployeeProfile {
    return {
        managerId: null,
        annualLeaveEntitlement: 20,
        leaveBalance: 20,
        jobTitle: null,
        employmentStartDate: null,
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
    id: 'admin-1', userName: 'systemadmin@annualleave.com', email: 'systemadmin@annualleave.com',
    displayName: 'Admin User', imageUrl: '', roles: ['System Administrator'],
}

const MANAGER: UserInfo = {
    id: 'manager-1', userName: 'manager1@annualleave.com', email: 'manager1@annualleave.com',
    displayName: 'Manager One', imageUrl: '', roles: ['Manager'], departmentId: FINANCE.id,
}

const HR: UserInfo = {
    id: 'hr-1', userName: 'hradmin@annualleave.com', email: 'hradmin@annualleave.com',
    displayName: 'Helen HR', imageUrl: '', roles: ['HR Administrator'],
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

async function renderPage(user: UserInfo = ADMIN) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <QueryClientProvider client={queryClient}>
            <AllLeaveAdminPage user={user} />
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

    /* Regression: a per-child type's `defaultAllowance` is 0 by migration on purpose,
       which used to read as "has no allowance on record" with a bar pinned at empty —
       wrong for a type that grants 18 weeks per child. */
    it('quotes the per-child policy instead of "no allowance on record"', async () => {
        api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, SICK_LEAVE_TYPE, PATERNITY_LEAVE_TYPE] as never)
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'pt1', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                leaveTypeId: PATERNITY_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        expect(screen.queryByText(/has no allowance on record/)).not.toBeInTheDocument()
        expect(screen.queryByText(/left after/)).not.toBeInTheDocument()

        const policyText = screen.getByText('18 weeks per child · max 5 weeks/year')
        expect(policyText).toBeInTheDocument()
        const cell = policyText.parentElement!
        expect(cell.getAttribute('title')).toBe(
            'Paternity Leave: 18 weeks per child · max 5 weeks/year — tracked per child, not a per-employee balance')
    })
})

/**
 * The attachment policy gates approval on the server (`AttachmentPolicyRule`), so
 * an undocumented request on a Required type cannot be approved from here — not
 * one at a time, and not swept up in a bulk approval alongside the rest.
 */
describe('AllLeaveAdminPage — a required document holds approval', () => {
    const MILITARY_LEAVE_TYPE = {
        ...ANNUAL_LEAVE_TYPE, id: 3, name: 'Military Leave', colorKey: 'military',
        affectsBalance: false, attachmentPolicy: 'Required',
    } as const

    const UNDOCUMENTED = leave({
        id: 'p5', employeeId: 'emp-2a', employeeName: 'Employee 2A', leaveTypeId: MILITARY_LEAVE_TYPE.id,
        startDate: monthOffset(1, 20), endDate: monthOffset(1, 22),
    })

    beforeEach(() => {
        api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, SICK_LEAVE_TYPE, MILITARY_LEAVE_TYPE] as never)
        api.getAnnualLeaves.mockResolvedValue([...PENDING, UNDOCUMENTED, ...DECIDED])
        api.updateLeaveStatus.mockResolvedValue(undefined as never)
    })

    /** The row that carries the reminder: walk up from it to the box holding its own Approve. */
    function heldRow() {
        let el: HTMLElement | null = screen.getByText(/Awaiting document/)
        while (el && !within(el).queryByRole('button', { name: 'Approve' })) el = el.parentElement
        if (!el) throw new Error('No row holds the Awaiting document reminder.')
        return el
    }

    it('disables Approve on the row and says why', async () => {
        await renderPage()

        const row = heldRow()
        expect(within(row).getByRole('button', { name: 'Approve' })).toBeDisabled()
        expect(within(row).getByRole('button', { name: 'Reject' })).toBeEnabled()
    })

    it('leaves a documented request on the same type approvable', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            ...PENDING,
            { ...UNDOCUMENTED, evidenceUrl: '/api/files/8f2c1b6e-0000-4000-8000-000000000001' },
            ...DECIDED,
        ])
        await renderPage()

        expect(screen.queryByText(/Awaiting document/)).not.toBeInTheDocument()
        for (const approve of screen.getAllByRole('button', { name: 'Approve' })) expect(approve).toBeEnabled()
    })

    it('skips an undocumented request in a bulk approval', async () => {
        await renderPage()

        for (const box of screen.getAllByRole('checkbox')) fireEvent.click(box)
        fireEvent.click(await screen.findByRole('button', { name: /Approve Selected/ }))

        // The viewer's own request (p3, admin-1's) is skipped too: nobody decides
        // their own leave (UpdateLeaveStatus.OwnLeaveMessage).
        const othersPending = PENDING.filter((l) => l.employeeId !== ADMIN.id)
        await waitFor(() => expect(api.updateLeaveStatus).toHaveBeenCalledTimes(othersPending.length))
        const approvedIds = api.updateLeaveStatus.mock.calls.map(([id]) => id)
        expect(approvedIds).not.toContain(UNDOCUMENTED.id)
        expect(approvedIds).not.toContain('p3')
        expect(approvedIds).toEqual(expect.arrayContaining(othersPending.map((l) => l.id)))
    })
})

/**
 * A Manager cannot decide a request that is with HR — they already had their say
 * at stage one (`ApprovalStageRule`) — so a row `AwaitingHrApproval` must not join
 * a bulk approve from a Manager's own checkbox.
 */
describe('AllLeaveAdminPage — a Manager cannot bulk-approve a row that is with HR', () => {
    const WITH_HR = leave({
        id: 'p6', employeeId: 'emp-2a', employeeName: 'Employee 2A',
        startDate: monthOffset(1, 20), endDate: monthOffset(1, 22),
        status: 'AwaitingHrApproval',
    })

    beforeEach(() => {
        api.getAnnualLeaves.mockResolvedValue([PENDING[0], WITH_HR])
    })

    it('disables the checkbox on the Awaiting-HR row', async () => {
        await renderPage(MANAGER)

        const checkboxes = screen.getAllByRole('checkbox') as HTMLInputElement[]
        expect(checkboxes).toHaveLength(2)
        expect(checkboxes.filter((box) => box.disabled)).toHaveLength(1)
    })
})

/**
 * A non-balance type with `proRateFirstYear` on is measured against its allowance
 * scaled for a first-year joiner — the server never enforces these allowances, so
 * the row is where the switch shows. The hover says the figure is pro-rated rather
 * than calling it an override, which is a different thing (an entitlement set per
 * person, which no longer exists).
 */
describe('AllLeaveAdminPage pro-rated first year', () => {
    it("measures a pro-rated non-balance type against this year's scaled allowance", async () => {
        const now = new Date()
        const firstOfMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`
        api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, { ...SICK_LEAVE_TYPE, proRateFirstYear: true }] as never)
        api.getEmployeeProfiles.mockResolvedValue([
            ...PROFILES.filter((p) => p.userId !== 'emp-2a'),
            profile({ id: 'pr1', userId: 'emp-2a', displayName: 'Employee 2A', departmentId: FINANCE.id, employmentStartDate: firstOfMonth }),
        ])
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 's3', employeeId: 'emp-2a', employeeName: 'Employee 2A',
                leaveTypeId: SICK_LEAVE_TYPE.id,
                startDate: sameYear(11, 3), endDate: sameYear(11, 5), totalDays: 3,
            }),
        ])
        await renderPage()

        // Whatever month the suite runs in, the helper says what 10 days pro-rates to.
        const expected = proRateFirstYearAllowance(10, firstOfMonth, APP_SETTINGS.leaveYearStartMonth)
        const cell = screen.getByText(`0/${expected} used`).parentElement!
        expect(cell.getAttribute('title')).toBe(
            `Sick Leave: 0 of ${expected} days/year used this year · `
            + `Pro-rated for the first year from 10 days/year · `
            + 'Tracked separately — not deducted from the annual balance')
    })
})

/**
 * Mirror of `ApprovalStageRule` and `CancellationRule`: the manager stage is the
 * manager's, so an HR Administrator's page leaves out a Pending request on a type
 * that asks for the manager and shows it once decided. What HR holds over an
 * approved request is cancelling it before it starts.
 */
describe('AllLeaveAdminPage — an HR Administrator sees the manager\'s requests once decided', () => {
    it('leaves the rows that are with the manager out of the queue and the counts', async () => {
        await renderPage(HR)

        // Every PENDING fixture is on Annual Leave, which asks for the manager.
        expect(renderedPendingRows()).toBe(0)
        expect(statCardValue('⏳ Awaiting Review')).toBe('0')
        expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
        // The approved row is still there; the manager's rejection is not.
        expect(screen.getByText('Employee 2A')).toBeInTheDocument()
        expect(screen.queryByText('Employee 1A')).not.toBeInTheDocument()
    })

    it("leaves a manager's rejection out, and keeps HR's own", async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({ id: 'r-mgr', employeeId: 'emp-2a', employeeName: 'Employee 2A', status: 'Rejected', startDate: monthOffset(1, 2), endDate: monthOffset(1, 3) }),
            leave({ id: 'r-hr', employeeId: 'emp-2b', employeeName: 'Employee 2B', status: 'Rejected', startDate: monthOffset(1, 9), endDate: monthOffset(1, 10) }),
        ])
        api.getLeaveStatusHistories.mockResolvedValue([
            { id: 'h1', annualLeaveId: 'r-mgr', employeeId: 'emp-2a', employeeName: 'Employee 2A', changedByUserId: 'manager-1', changedByUserName: 'Manager One', leaveTypeName: 'Annual Leave', oldStatus: 'Pending', newStatus: 'Rejected', comment: 'Too many away', changedAt: monthOffset(0, 1) },
            { id: 'h2', annualLeaveId: 'r-hr', employeeId: 'emp-2b', employeeName: 'Employee 2B', changedByUserId: 'hr-1', changedByUserName: 'Helen HR', leaveTypeName: 'Annual Leave', oldStatus: 'AwaitingHrApproval', newStatus: 'Rejected', comment: 'Policy', changedAt: monthOffset(0, 1) },
        ])
        await renderPage(HR)

        expect(screen.queryByText('Employee 2A')).not.toBeInTheDocument()
        expect(screen.getByText('Employee 2B')).toBeInTheDocument()
        expect(screen.getByText('"Policy"')).toBeInTheDocument()
    })

    it('has no status tabs — the page already holds only what is theirs', async () => {
        await renderPage(HR)

        expect(screen.queryByRole('button', { name: /Conflicts/ })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /Urgent/ })).not.toBeInTheDocument()
    })

    it('keeps the status tabs for a System Administrator', async () => {
        await renderPage(ADMIN)

        expect(screen.getByRole('button', { name: /Conflicts/ })).toBeInTheDocument()
    })

    it('queues a request that is with HR, with Approve to hand', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            ...PENDING,
            leave({
                id: 'h1', employeeId: 'emp-2b', employeeName: 'Employee 2B', status: 'AwaitingHrApproval',
                startDate: monthOffset(1, 12), endDate: monthOffset(1, 13),
            }),
        ])
        await renderPage(HR)

        expect(renderedPendingRows()).toBe(1)
        expect(statCardValue('⏳ Awaiting Review')).toBe('1')
        expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled()
    })

    it('offers Cancel on an approved leave that has not started, and not on one that has', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            ...DECIDED, // d1 is approved and four months ago
            leave({
                id: 'a-future', employeeId: 'emp-2b', employeeName: 'Employee 2B', status: 'Approved',
                startDate: monthOffset(1, 12), endDate: monthOffset(1, 13), totalDays: 2,
            }),
        ])
        await renderPage(HR)

        const cancels = screen.getAllByRole('button', { name: 'Cancel' })
        expect(cancels).toHaveLength(1)
        expect(cancels[0].closest('[class]')!.parentElement!.parentElement!.textContent).toContain('Employee 2B')
    })

    it('does not offer Cancel to a System Administrator, who neither files nor decides leave', async () => {
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'a-future', employeeId: 'emp-2b', employeeName: 'Employee 2B', status: 'Approved',
                startDate: monthOffset(1, 12), endDate: monthOffset(1, 13), totalDays: 2,
            }),
        ])
        await renderPage(ADMIN)

        expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    })

    it('cancels with a reason the employee will see', async () => {
        api.updateLeaveStatus.mockResolvedValue(undefined)
        api.getAnnualLeaves.mockResolvedValue([
            leave({
                id: 'a-future', employeeId: 'emp-2b', employeeName: 'Employee 2B', status: 'Approved',
                startDate: monthOffset(1, 12), endDate: monthOffset(1, 13), totalDays: 2,
            }),
        ])
        await renderPage(HR)

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
        expect(await screen.findByText('Cancel approved leave')).toBeInTheDocument()

        const confirm = screen.getByRole('button', { name: 'Confirm Cancel' })
        expect(confirm).toBeDisabled()
        fireEvent.change(screen.getByPlaceholderText('Reason for cancelling (required)'), { target: { value: 'Project deadline moved' } })
        fireEvent.click(confirm)

        await waitFor(() => expect(api.updateLeaveStatus).toHaveBeenCalledWith('a-future', 'Cancelled', 'Project deadline moved'))
    })
})
