import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { StoreProvider } from '../../lib/mobx'
import type { ChildLeaveEntitlementSummary, EmployeeProfile, UserInfo } from '../../lib/types'
import ApplyLeavePage from './ApplyLeavePage'

/**
 * `/apply-leave` is the only leave-creation surface a user can actually reach —
 * `AnnualLeaveForm`, where the child picker was originally built, is rendered by
 * `AnnualLeaveList`, which nothing renders. So this page is where the per-child
 * wiring has to be proven: the server sets `ChildId` from the leave type and then
 * refuses a per-child request that names no child, which means a paternity request
 * from this page failed outright until it started sending one.
 *
 * These tests fail if `childId` is dropped from the payload, if the picker stops
 * appearing for a per-child type, or if a child leaks onto a type that has no
 * per-child entitlement.
 */
vi.mock('../../lib/api', () => ({
    createAnnualLeave: vi.fn(),
    getAnnualLeaves: vi.fn(),
    getChildLeaveEntitlements: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getHolidays: vi.fn(),
    getLeaveTypes: vi.fn(),
    getTeammates: vi.fn(),
    uploadLeaveEvidence: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', requiresApproval: true, isActive: true, affectsBalance: true,
    icon: '', colorKey: 'primary', description: '', paid: true, attachmentPolicy: 'None',
    defaultAllowance: 25, allowanceUnit: 'days/year', maxCarryoverDays: 0,
    perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0,
    accrualNotes: '', minNoticeDays: 0,
    maxConsecutiveDays: 0, halfDayAllowed: false, eligibilityNotes: '', eligibilityScope: 'All',
} as const

/** As configured by migration: the budget is the per-child one, not `defaultAllowance`. */
const PATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 3, name: 'Paternity Leave', colorKey: 'paternity',
    defaultAllowance: 0, affectsBalance: false,
    perChildEntitlement: true, perChildTotalWeeks: 18, perChildWeeksPerYear: 5, childEligibleUntilAge: 15,
} as const

const USER: UserInfo = {
    id: 'emp-1', userName: 'employee@worktrack.com', email: 'employee@worktrack.com',
    displayName: 'Andreas Georgiou', imageUrl: '', roles: ['Employee'], departmentId: 2,
}

const PROFILE: EmployeeProfile = {
    id: 'pr1', userId: USER.id, displayName: USER.displayName, departmentId: 2,
    managerId: null, annualLeaveEntitlement: 25, leaveBalance: 25,
    jobTitle: null, createdAt: '2026-01-01T00:00:00',
}

const ENTITLEMENTS: ChildLeaveEntitlementSummary = {
    leaveTypeId: PATERNITY_LEAVE_TYPE.id,
    leaveTypeName: PATERNITY_LEAVE_TYPE.name,
    eligibleChildCount: 1,
    totalRemainingDays: 90,
    thisYearCapDays: 25,
    thisYearRemainingDays: 25,
    children: [
        {
            childId: 'child-1', name: 'Andreas Jr', dateOfBirth: '2019-03-04', ageYears: 7,
            isEligible: true, lastEligibleDate: '2034-03-03',
            totalDays: 90, totalWeeks: 18, usedDays: 0, remainingDays: 90,
            thisYearCapDays: 25, thisYearUsedDays: 0, thisYearRemainingDays: 25,
            leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
        },
    ],
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE, PATERNITY_LEAVE_TYPE] as never)
    api.getEmployeeProfiles.mockResolvedValue([PROFILE])
    api.getAnnualLeaves.mockResolvedValue([])
    api.getTeammates.mockResolvedValue([])
    api.getHolidays.mockResolvedValue([])
    api.getChildLeaveEntitlements.mockResolvedValue(ENTITLEMENTS)
    api.createAnnualLeave.mockResolvedValue('new-leave-id' as never)
})

async function renderPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <ApplyLeavePage user={USER} />
            </QueryClientProvider>
        </StoreProvider>,
    )
    // The type cards only exist once the leave-type query settles, and the page
    // auto-selects Annual Leave at that point.
    await screen.findByRole('button', { name: /paternity leave/i })
}

/**
 * The first pair of consecutive weekdays in the displayed (current) month. Computed
 * rather than hard-coded because the calendar opens on whatever month today is in,
 * and weekend cells are not clickable. Holidays are mocked away as an empty list.
 */
function consecutiveWeekdays() {
    const now = new Date()
    const daysInMonth = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate()
    const isWeekday = (day: number) => {
        const dow = new Date(now.getFullYear(), now.getMonth(), day).getDay()
        return dow !== 0 && dow !== 6
    }
    for (let day = 1; day < daysInMonth; day++) {
        if (isWeekday(day) && isWeekday(day + 1)) return [String(day), String(day + 1)] as const
    }
    throw new Error('No consecutive weekdays in the current month — impossible, but fail loudly.')
}

/** Day cells are plain divs holding the day number, so scope the query to the calendar. */
function pickDates() {
    const calendar = screen.getByText('Mon').parentElement!.parentElement!
    const [start, end] = consecutiveWeekdays()
    fireEvent.click(within(calendar).getByText(start))
    fireEvent.click(within(calendar).getByText(end))
}

/** A MUI Select opens on mouseDown and renders its options into a portal. */
async function chooseChild(optionText: RegExp) {
    const combobox = await screen.findByRole('combobox', { name: /child/i })
    await waitFor(() => expect(combobox).not.toHaveAttribute('aria-disabled', 'true'))
    fireEvent.mouseDown(combobox)
    fireEvent.click(await screen.findByText(optionText))
}

describe('ApplyLeavePage — per-child leave', () => {
    it('sends the chosen child with a paternity request', async () => {
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()
        await chooseChild(/Andreas Jr/)

        fireEvent.click(await screen.findByRole('button', { name: /submit for approval/i }))

        await waitFor(() => expect(api.createAnnualLeave).toHaveBeenCalledTimes(1))
        expect(api.createAnnualLeave).toHaveBeenCalledWith(
            expect.objectContaining({
                employeeId: USER.id,
                leaveTypeId: PATERNITY_LEAVE_TYPE.id,
                // The whole point: without this the server refuses the request with
                // "Select the child this Paternity Leave is for."
                childId: 'child-1',
            }),
        )
    })

    it('shows no child picker, and sends no child, for a type with no per-child entitlement', async () => {
        await renderPage()

        // Annual Leave is the page's default selection.
        pickDates()

        expect(screen.queryByRole('combobox', { name: /child/i })).not.toBeInTheDocument()

        fireEvent.click(await screen.findByRole('button', { name: /submit for approval/i }))

        await waitFor(() => expect(api.createAnnualLeave).toHaveBeenCalledTimes(1))
        expect(api.createAnnualLeave).toHaveBeenCalledWith(
            expect.objectContaining({ leaveTypeId: ANNUAL_LEAVE_TYPE.id }),
        )
        expect(api.createAnnualLeave.mock.calls[0][0].childId).toBeUndefined()
    })

    /**
     * Dates alone are not enough for a per-child type, and the button used to say
     * "Pick dates to continue" once they were picked — the one instruction that could
     * not help.
     */
    it('keeps submit disabled until a child is chosen', async () => {
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()

        const blocked = await screen.findByRole('button', { name: /select a child to continue/i })
        expect(blocked).toBeDisabled()

        await chooseChild(/Andreas Jr/)

        await waitFor(() =>
            expect(screen.getByRole('button', { name: /submit for approval/i })).not.toBeDisabled(),
        )
    })

    /**
     * The picker reports back when it has no real choice to offer — here, no children
     * on file. Leaving submit enabled would hand the user a button that only ever
     * produces a server refusal they cannot act on from this screen.
     */
    it('disables submit when the employee has no children on file', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue({
            ...ENTITLEMENTS, eligibleChildCount: 0, totalRemainingDays: 0, children: [],
        })

        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()

        expect(await screen.findByText(/add your children in edit profile/i)).toBeInTheDocument()
        await waitFor(() => {
            const submit = screen.getByRole('button', { name: /submit for approval|to continue/i })
            expect(submit).toBeDisabled()
        })
        expect(api.createAnnualLeave).not.toHaveBeenCalled()
    })

    /**
     * A child chosen for a per-child type must not ride along on a request for a
     * type that has none — the server would clear it anyway, and sending it invites
     * the ledger to charge a child for leave never taken against the per-child cap.
     */
    it('sends no child once the leave type is switched away from a per-child one', async () => {
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()
        await chooseChild(/Andreas Jr/)

        fireEvent.click(screen.getByRole('button', { name: /annual leave/i }))
        await waitFor(() =>
            expect(screen.queryByRole('combobox', { name: /child/i })).not.toBeInTheDocument(),
        )

        fireEvent.click(await screen.findByRole('button', { name: /submit for approval/i }))

        await waitFor(() => expect(api.createAnnualLeave).toHaveBeenCalledTimes(1))
        expect(api.createAnnualLeave).toHaveBeenCalledWith(
            expect.objectContaining({ leaveTypeId: ANNUAL_LEAVE_TYPE.id }),
        )
        expect(api.createAnnualLeave.mock.calls[0][0].childId).toBeUndefined()
    })
})
