import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ChildLeaveEntitlementSummary, UserInfo } from '../../lib/types'
import AnnualLeaveForm from './AnnualLeaveForm'

/**
 * The same gender + eligible-child rule the apply page applies, on the other form
 * that files a leave request.
 *
 * With one carve-out this file exists to pin: an admin filing **on behalf of**
 * somebody else sees every type. The rule is about what an employee is offered
 * for themselves, and the caller's own gender says nothing about the employee the
 * request is for — filtering on it there would hide the very type the admin was
 * asked to file.
 *
 * Note this dialog is currently unreachable in the running app: `AnnualLeaveCard`
 * and `AnnualLeaveList` render it, and nothing renders them. It is gated anyway,
 * so that wiring the surface back up cannot quietly reopen the rule.
 */
vi.mock('../../lib/api', () => ({
    createAnnualLeave: vi.fn(),
    editAnnualLeave: vi.fn(),
    getAdminUsers: vi.fn(),
    getChildLeaveEntitlements: vi.fn(),
    getLeaveTypes: vi.fn(),
    uploadLeaveEvidence: vi.fn(),
}))

vi.mock('../../lib/mobx', () => ({ useStore: vi.fn() }))

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', requiresApproval: true, isActive: true, affectsBalance: true,
    icon: '', colorKey: 'primary', description: '', paid: true, attachmentPolicy: 'None',
    defaultAllowance: 25, allowanceUnit: 'days/year', maxCarryoverDays: 0,
    perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0,
    accrualNotes: '', minNoticeDays: 0, maxConsecutiveDays: 0, halfDayAllowed: false,
    eligibilityNotes: '', eligibilityScope: 'All',
} as const

const MATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 2, name: 'Maternity Leave', affectsBalance: false,
    defaultAllowance: 90, allowanceUnit: 'days/event',
} as const

const PATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 3, name: 'Paternity Leave', affectsBalance: false,
    defaultAllowance: 0, allowanceUnit: 'weeks/child',
    perChildEntitlement: true, perChildTotalWeeks: 18, perChildWeeksPerYear: 5, childEligibleUntilAge: 15,
} as const

const USER: UserInfo = {
    id: 'emp-1', userName: 'employee@worktrack.com', email: 'employee@worktrack.com',
    displayName: 'Andreas Georgiou', imageUrl: '', roles: ['Employee'], departmentId: 2,
}

const ENTITLEMENTS: ChildLeaveEntitlementSummary = {
    leaveTypeId: PATERNITY_LEAVE_TYPE.id,
    leaveTypeName: PATERNITY_LEAVE_TYPE.name,
    eligibleChildCount: 1,
    totalRemainingDays: 90,
    thisYearCapDays: 25,
    thisYearRemainingDays: 25,
    children: [{
        childId: 'child-1', name: 'Andreas Jr', dateOfBirth: '2019-03-04', ageYears: 7,
        isEligible: true, lastEligibleDate: '2034-03-03',
        totalDays: 90, totalWeeks: 18, usedDays: 0, remainingDays: 90,
        thisYearCapDays: 25, thisYearUsedDays: 0, thisYearRemainingDays: 25,
        leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
    }],
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getLeaveTypes.mockResolvedValue(
        [ANNUAL_LEAVE_TYPE, MATERNITY_LEAVE_TYPE, PATERNITY_LEAVE_TYPE] as never,
    )
    api.getAdminUsers.mockResolvedValue([] as never)
    api.getChildLeaveEntitlements.mockResolvedValue(ENTITLEMENTS)
})

/**
 * Renders the dialog for a signed-in user of the given gender. The MUI select
 * lists its options in the DOM only once opened, so the assertions read the
 * option list rather than the rendered value.
 */
async function renderForm({
    gender, isAdmin = false, eligibleChildren = 1,
}: { gender: UserInfo['gender']; isAdmin?: boolean; eligibleChildren?: number }) {
    api.getChildLeaveEntitlements.mockResolvedValue({
        ...ENTITLEMENTS,
        eligibleChildCount: eligibleChildren,
        children: eligibleChildren > 0 ? ENTITLEMENTS.children : [],
    })

    mobx.useStore.mockReturnValue({ authStore: { user: { ...USER, gender } } } as never)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={queryClient}>
            <AnnualLeaveForm open onClose={() => {}} isAdmin={isAdmin} />
        </QueryClientProvider>,
    )

    const select = await screen.findByRole('combobox', { name: /leave type/i })
    await waitFor(() => expect(select).not.toHaveAttribute('aria-disabled', 'true'))
    return select
}

/** The names the Leave Type select currently offers. */
function offeredTypeNames() {
    return screen.getAllByRole('option').map((option) => option.textContent)
}

describe('AnnualLeaveForm — who is offered parental leave', () => {
    it('offers a female employee maternity leave and not paternity leave', async () => {
        const select = await renderForm({ gender: 'Female' })
        fireEvent.mouseDown(select)

        await waitFor(() => expect(offeredTypeNames()).toContain('Maternity Leave'))
        expect(offeredTypeNames()).not.toContain('Paternity Leave')
    })

    it('offers a male employee paternity leave and not maternity leave', async () => {
        const select = await renderForm({ gender: 'Male' })
        fireEvent.mouseDown(select)

        await waitFor(() => expect(offeredTypeNames()).toContain('Paternity Leave'))
        expect(offeredTypeNames()).not.toContain('Maternity Leave')
    })

    it('offers neither type to an employee with no eligible children', async () => {
        const select = await renderForm({ gender: 'Female', eligibleChildren: 0 })
        fireEvent.mouseDown(select)

        await waitFor(() => expect(offeredTypeNames()).toContain('Annual Leave'))
        expect(offeredTypeNames()).not.toContain('Maternity Leave')
        expect(offeredTypeNames()).not.toContain('Paternity Leave')
    })

    it('offers both types when the gender was never recorded', async () => {
        const select = await renderForm({ gender: null })
        fireEvent.mouseDown(select)

        await waitFor(() => expect(offeredTypeNames()).toContain('Maternity Leave'))
        expect(offeredTypeNames()).toContain('Paternity Leave')
    })

    /**
     * The carve-out. An admin filing for somebody else is not the subject of the
     * rule, and their own gender and children say nothing about the employee they
     * are filing for — so the list stays whole, and the server has the last word.
     */
    it('offers an admin filing on behalf of someone else every type', async () => {
        const select = await renderForm({ gender: 'Male', isAdmin: true, eligibleChildren: 0 })
        fireEvent.mouseDown(select)

        await waitFor(() => expect(offeredTypeNames()).toContain('Annual Leave'))
        expect(offeredTypeNames()).toContain('Maternity Leave')
        expect(offeredTypeNames()).toContain('Paternity Leave')
    })
})
