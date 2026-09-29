import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ChildLeaveEntitlementSummary, EmployeeProfile, UserInfo } from '../../lib/types'
import DashboardHome from './DashboardHome'

/**
 * The employee dashboard's "Leave balance" card answers the same two questions My
 * Leave's panel does — which types this employee may request, and how much of each
 * they have left — so it reads them from the same hook and the same row builder.
 * It used to list every active type and measure all of them against the pooled
 * annual entitlement, which offered a woman paternity leave and reported a
 * per-child type as 0.
 */
vi.mock('../../lib/api', () => ({
    getIdleTaskPeople: vi.fn(async () => []),
    getAnnualLeaves: vi.fn(),
    getAppSettings: vi.fn(),
    getChildLeaveEntitlements: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getLeaveTypes: vi.fn(),
    getMyTimesheets: vi.fn(),
}))

vi.mock('../../lib/mobx', () => ({ useStore: vi.fn() }))

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', requiresManagerApproval: true, isActive: true, affectsBalance: true,
    icon: '', colorKey: 'primary', description: '', paid: true, attachmentPolicy: 'None',
    defaultAllowance: 23, allowanceUnit: 'days/year', maxCarryoverDays: 0,
    perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0,
    accrualNotes: '', minNoticeDays: 0,
    maxConsecutiveDays: 0, halfDayAllowed: false, availableTo: 'Both',
} as const

/** Configured per child: 4 weeks (20 business days) each, until age 4. */
const MATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 3, name: 'Maternity Leave', availableTo: 'Female', affectsBalance: false, defaultAllowance: 0,
    perChildEntitlement: true, perChildTotalWeeks: 4, perChildWeeksPerYear: 1, childEligibleUntilAge: 4,
} as const

const PATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 4, name: 'Paternity Leave', availableTo: 'Male', affectsBalance: false, defaultAllowance: 0,
    perChildEntitlement: true, perChildTotalWeeks: 18, perChildWeeksPerYear: 5, childEligibleUntilAge: 15,
} as const

const USER: UserInfo = {
    id: 'emp-1', userName: 'employee@worktrack.com', email: 'employee@worktrack.com',
    displayName: 'Maria Georgiou', imageUrl: '', roles: ['Employee'], departmentId: 2,
    gender: 'Female',
}

const PROFILE: EmployeeProfile = {
    id: 'pr1', userId: USER.id, displayName: USER.displayName, departmentId: 2,
    managerId: null, annualLeaveEntitlement: 23, leaveBalance: 23,
    jobTitle: null, employmentStartDate: null, createdAt: '2026-01-01T00:00:00',
}

function summaryFor(leaveType: { id: number; name: string }, daysPerChild: number): ChildLeaveEntitlementSummary {
    const child = (childId: string, name: string, dateOfBirth: string, ageYears: number) => ({
        childId, name, dateOfBirth, ageYears,
        isEligible: true, lastEligibleDate: '2030-01-01',
        totalDays: daysPerChild, totalWeeks: daysPerChild / 5, usedDays: 0, remainingDays: daysPerChild,
        thisYearCapDays: 5, thisYearUsedDays: 0, thisYearRemainingDays: 5,
        leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
    })
    return {
        leaveTypeId: leaveType.id,
        leaveTypeName: leaveType.name,
        eligibleChildCount: 2,
        totalRemainingDays: daysPerChild * 2,
        thisYearCapDays: 5,
        thisYearRemainingDays: 5,
        children: [
            child('child-1', 'Elena', '2023-04-02', 3),
            child('child-2', 'Andreas', '2026-01-09', 0),
        ],
    }
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, MATERNITY_LEAVE_TYPE, PATERNITY_LEAVE_TYPE] as never)
    api.getEmployeeProfiles.mockResolvedValue([PROFILE])
    api.getAnnualLeaves.mockResolvedValue([])
    api.getMyTimesheets.mockResolvedValue([])
    api.getAppSettings.mockResolvedValue({ leaveYearStartMonth: 1 } as never)
    api.getChildLeaveEntitlements.mockImplementation(async (_employeeId?: string, leaveTypeId?: number) =>
        leaveTypeId === PATERNITY_LEAVE_TYPE.id
            ? summaryFor(PATERNITY_LEAVE_TYPE, 90)
            : summaryFor(MATERNITY_LEAVE_TYPE, 20))
    mobx.useStore.mockReturnValue({ authStore: { user: USER }, uiStore: {} } as never)
})

async function renderDashboard() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/dashboard']}>
            <QueryClientProvider client={queryClient}>
                <DashboardHome />
            </QueryClientProvider>
        </MemoryRouter>,
    )
    await screen.findByText('Leave balance')
}

/**
 * The balance card's own subtree, so nothing else on the dashboard answers for
 * it. The title sits in its own box inside the card's header, hence the two steps
 * back up to the card itself.
 */
function balanceCard() {
    const title = screen.getByText('Leave balance')
    return title.parentElement!.parentElement as HTMLElement
}

function balanceRow(name: string) {
    return within(balanceCard()).getByText(name).parentElement!.parentElement as HTMLElement
}

describe('EmployeeDashboard leave balance card', () => {
    it('leaves out a parental type the employee is not offered', async () => {
        await renderDashboard()

        await waitFor(() => expect(within(balanceCard()).queryByText('Maternity Leave')).not.toBeNull())
        expect(within(balanceCard()).queryByText('Paternity Leave')).toBeNull()
    })

    /* The card sits beside this year's figures, so a per-child row quotes what this
       leave year allows — the 5-day cap for each of two eligible children — rather
       than the 20 days each is entitled to across their whole eligibility. */
    it("measures a per-child type against its own ledger's yearly cap, not the pooled balance", async () => {
        await renderDashboard()

        await waitFor(() => expect(balanceRow('Maternity Leave').textContent).toContain('10/10'))
    })

    it('still measures annual leave against the pooled entitlement', async () => {
        await renderDashboard()

        expect(balanceRow('Annual Leave').textContent).toContain('23/23')
    })
})
