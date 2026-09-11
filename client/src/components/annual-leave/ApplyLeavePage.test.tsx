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

/**
 * As seeded: a flat 90-day allowance and no per-child ledger of its own, which is
 * why nothing else in the request path checks a child for it.
 */
const MATERNITY_LEAVE_TYPE = {
    ...ANNUAL_LEAVE_TYPE, id: 4, name: 'Maternity Leave', colorKey: 'maternity',
    defaultAllowance: 90, allowanceUnit: 'days/event', affectsBalance: false,
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

/**
 * The children are a grid of cards, not a dropdown. When more than one child
 * qualifies the cards are buttons and this clicks one; when only one does, the
 * picker has already chosen them and the card is read-only — so awaiting it is
 * just waiting for that to have happened.
 */
async function chooseChild(name: RegExp) {
    // The child's name also turns up in the picker caption and the over-cap
    // warning, so prefer the match that sits inside a card.
    const matches = await screen.findAllByText(name)
    const button = matches.map((match) => match.closest('button')).find(Boolean)
    if (button) fireEvent.click(button)
}

/** Two children young enough to qualify, so there is a choice to be made. */
function twoEligibleChildren(): ChildLeaveEntitlementSummary {
    return {
        ...ENTITLEMENTS,
        eligibleChildCount: 2,
        totalRemainingDays: 180,
        children: [
            ENTITLEMENTS.children[0],
            { ...ENTITLEMENTS.children[0], childId: 'child-2', name: 'Sofia' },
        ],
    }
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

        expect(screen.queryByRole('button', { name: /Andreas Jr/ })).not.toBeInTheDocument()

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
    /**
     * Even with two children qualifying the form never asks. The cards are read
     * only, so the picker charges the request to one of them — see
     * `ChildLeavePicker`, which takes whichever entitlement expires soonest — and
     * submit unlocks on the dates alone.
     */
    it('never asks for a child, even when several qualify', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue(twoEligibleChildren())
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()

        await waitFor(() =>
            expect(screen.getByRole('button', { name: /submit for approval/i })).not.toBeDisabled(),
        )
        expect(screen.queryByRole('button', { name: /select a child to continue/i })).not.toBeInTheDocument()
    })

    /**
     * With only one child qualifying there is nothing to decide, so the picker
     * decides it and the form never asks. The step was previously a click that
     * could only be made one way.
     */
    it('never asks for a child when only one qualifies', async () => {
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()

        await waitFor(() =>
            expect(screen.getByRole('button', { name: /submit for approval/i })).not.toBeDisabled(),
        )
        expect(screen.queryByRole('button', { name: /select a child to continue/i })).not.toBeInTheDocument()
    })

    /**
     * This used to assert that the picker reported itself blocked and submit went
     * grey — an employee with no children could select Paternity Leave and be
     * stopped at the child field. They can no longer select it at all: the type is
     * not offered without an eligible child (see the eligibility tests below), so
     * the stronger guarantee replaces the weaker one.
     *
     * The blocked-picker path itself is still live and still tested, in
     * `ChildLeavePicker.test.tsx` — `AnnualLeaveForm` renders the picker on the
     * admin on-behalf path, where the type list is deliberately not filtered.
     */
    it('does not offer a per-child type at all when no children are on file', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue({
            ...ENTITLEMENTS, eligibleChildCount: 0, totalRemainingDays: 0, children: [],
        })

        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        render(
            <StoreProvider>
                <QueryClientProvider client={queryClient}>
                    <ApplyLeavePage user={USER} />
                </QueryClientProvider>
            </StoreProvider>,
        )
        await screen.findByRole('button', { name: /annual leave/i })

        await waitFor(() => expect(api.getChildLeaveEntitlements).toHaveBeenCalled())
        expect(screen.queryByRole('button', { name: /paternity leave/i })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /Andreas Jr/ })).not.toBeInTheDocument()
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
            expect(screen.queryByRole('button', { name: /Andreas Jr/ })).not.toBeInTheDocument(),
        )

        fireEvent.click(await screen.findByRole('button', { name: /submit for approval/i }))

        await waitFor(() => expect(api.createAnnualLeave).toHaveBeenCalledTimes(1))
        expect(api.createAnnualLeave).toHaveBeenCalledWith(
            expect.objectContaining({ leaveTypeId: ANNUAL_LEAVE_TYPE.id }),
        )
        expect(api.createAnnualLeave.mock.calls[0][0].childId).toBeUndefined()
    })
})

/**
 * Maternity and Paternity Leave are offered on two facts about the employee:
 * their recorded gender, and whether they have a child young enough to qualify.
 *
 * The server refuses a mismatched request either way
 * (`ParentalLeaveEligibility`), so these tests are about not offering a card that
 * can only end in a refusal — and, just as much, about not withholding one from
 * somebody entitled to it. The last case is the one that matters most: gender is
 * null on every account predating the field, and reading that as "neither" would
 * quietly strip parental leave from the whole company.
 */
describe('ApplyLeavePage — who is offered parental leave', () => {
    const maternityCard = () => screen.queryByRole('button', { name: /maternity leave/i })
    const paternityCard = () => screen.queryByRole('button', { name: /paternity leave/i })

    /**
     * Waits on Annual Leave rather than a parental card, since which of those
     * render is the thing under test. It is always offered and is the page's
     * default selection, so its presence means the type list has settled.
     */
    async function renderFor(gender: UserInfo['gender'], eligibleChildren = 1) {
        api.getLeaveTypes.mockResolvedValue(
            [ANNUAL_LEAVE_TYPE, MATERNITY_LEAVE_TYPE, PATERNITY_LEAVE_TYPE] as never,
        )
        api.getChildLeaveEntitlements.mockResolvedValue({
            ...ENTITLEMENTS,
            eligibleChildCount: eligibleChildren,
            children: eligibleChildren > 0
                ? ENTITLEMENTS.children
                : ENTITLEMENTS.children.map((child) => ({ ...child, isEligible: false, remainingDays: 0 })),
        })

        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        render(
            <StoreProvider>
                <QueryClientProvider client={queryClient}>
                    <ApplyLeavePage user={{ ...USER, gender }} />
                </QueryClientProvider>
            </StoreProvider>,
        )
        await screen.findByRole('button', { name: /annual leave/i })
        // The parental cards depend on the entitlements query too, so let it settle
        // before asserting on their absence.
        await waitFor(() => expect(api.getChildLeaveEntitlements).toHaveBeenCalled())
    }

    it('offers a female employee maternity leave and not paternity leave', async () => {
        await renderFor('Female')

        expect(maternityCard()).toBeInTheDocument()
        await waitFor(() => expect(paternityCard()).not.toBeInTheDocument())
    })

    it('offers a male employee paternity leave and not maternity leave', async () => {
        await renderFor('Male')

        expect(paternityCard()).toBeInTheDocument()
        await waitFor(() => expect(maternityCard()).not.toBeInTheDocument())
    })

    it('offers neither type to an employee with no eligible children', async () => {
        await renderFor('Female', 0)

        await waitFor(() => expect(maternityCard()).not.toBeInTheDocument())
        expect(paternityCard()).not.toBeInTheDocument()
    })

    it('offers both types when the gender was never recorded', async () => {
        await renderFor(null)

        expect(maternityCard()).toBeInTheDocument()
        expect(paternityCard()).toBeInTheDocument()
    })

    /**
     * The filter must not break the type it leaves standing. Maternity Leave keeps
     * no per-child ledger, so it asks for no child — the request is the ordinary
     * one, against a card that is only on screen because of the rule above.
     */
    it('lets a female employee file the maternity leave she is offered', async () => {
        await renderFor('Female')

        fireEvent.click(maternityCard()!)
        pickDates()

        fireEvent.click(await screen.findByRole('button', { name: /submit for approval/i }))

        await waitFor(() => expect(api.createAnnualLeave).toHaveBeenCalledTimes(1))
        expect(api.createAnnualLeave).toHaveBeenCalledWith(
            expect.objectContaining({ leaveTypeId: MATERNITY_LEAVE_TYPE.id }),
        )
    })
})

/**
 * The page has two ledgers to answer to and used to know only one. `isInsufficient`
 * is gated on `affectsBalance`, which is false for a per-child type — so a paternity
 * request for more days than the child has left sailed past submit and the summary
 * announced "All clear — plenty of balance", meaning the pooled annual balance it
 * had not touched.
 *
 * The server refuses these (`CheckPerChildEntitlementAsync` enforces both the
 * lifetime and the yearly cap at creation), so the only question was whether the
 * user found out here or after pressing a button.
 */
describe('ApplyLeavePage — the per-child cap gates submit', () => {
    /** The ledger with `remaining` business days left for the one child, both caps. */
    function withRemaining(remaining: number) {
        api.getChildLeaveEntitlements.mockResolvedValue({
            ...ENTITLEMENTS,
            totalRemainingDays: remaining,
            thisYearRemainingDays: remaining,
            children: [{
                ...ENTITLEMENTS.children[0],
                usedDays: 90 - remaining,
                remainingDays: remaining,
                thisYearRemainingDays: remaining,
            }],
        })
    }

    /** Selects paternity leave and picks two consecutive weekdays — two business days. */
    async function requestTwoDaysOfPaternityLeave() {
        await renderPage()
        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()
        await chooseChild(/Andreas Jr/)
    }

    it('disables submit when the request is longer than the child has left', async () => {
        withRemaining(1)

        await requestTwoDaysOfPaternityLeave()

        await waitFor(() => {
            const submit = screen.getByRole('button', { name: /submit for approval|to continue/i })
            expect(submit).toBeDisabled()
        })
        expect(api.createAnnualLeave).not.toHaveBeenCalled()
    })

    it('says whose entitlement is short, and by how much', async () => {
        withRemaining(1)

        await requestTwoDaysOfPaternityLeave()

        const warning = await screen.findByText(/not enough paternity leave/i)
        expect(warning.parentElement?.textContent).toMatch(/Andreas Jr/)
        expect(warning.parentElement?.textContent).toMatch(/1 day left/)
    })

    /** The reassurance was the worst part: it described a ledger the request never touched. */
    it('does not call a request the server will refuse "all clear"', async () => {
        withRemaining(1)

        await requestTwoDaysOfPaternityLeave()

        await waitFor(() => expect(screen.getByText(/not enough paternity leave/i)).toBeInTheDocument())
        expect(screen.queryByText(/all clear/i)).not.toBeInTheDocument()
    })

    it('still allows a request that fits inside what is left', async () => {
        withRemaining(5)

        await requestTwoDaysOfPaternityLeave()

        await waitFor(() =>
            expect(screen.getByRole('button', { name: /submit for approval/i })).not.toBeDisabled(),
        )
        expect(screen.queryByText(/not enough paternity leave/i)).not.toBeInTheDocument()
    })

    /**
     * The yearly cap is the tighter of the two here. Quoting the lifetime remainder
     * while the year's is exhausted would promise days the server refuses.
     */
    it('measures against the yearly cap when it is the tighter one', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue({
            ...ENTITLEMENTS,
            children: [{
                ...ENTITLEMENTS.children[0],
                remainingDays: 90,
                thisYearUsedDays: 24,
                thisYearRemainingDays: 1,
            }],
        })

        await requestTwoDaysOfPaternityLeave()

        await waitFor(() => {
            const submit = screen.getByRole('button', { name: /submit for approval|to continue/i })
            expect(submit).toBeDisabled()
        })
    })
})

/**
 * The summary panel quotes one balance, and for a per-child type it quoted the
 * wrong one: "Balance after 23 / 23", the pooled annual balance, beside a paternity
 * request that does not touch it.
 *
 * What belongs there is the per-child ledger, totalled over **every eligible
 * child** — that is what the employee has to spend on this type. It does not
 * depend on which child is selected: two children under 15 are worth two lots of
 * the entitlement whether or not one of them has been picked yet. Which child a
 * particular request is charged to is the over-cap warning's business, not this
 * row's.
 */
describe('ApplyLeavePage — the summary quotes the ledger the request draws on', () => {
    /** Two eligible children, 90 days each — 180 between them. */
    const TWO_CHILDREN: ChildLeaveEntitlementSummary = {
        ...ENTITLEMENTS,
        eligibleChildCount: 2,
        totalRemainingDays: 180,
        children: [
            ENTITLEMENTS.children[0],
            {
                ...ENTITLEMENTS.children[0],
                childId: 'child-2',
                name: 'Maria',
                dateOfBirth: '2014-02-11',
                ageYears: 12,
            },
        ],
    }

    it('totals every eligible child, not just the one picked', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue(TWO_CHILDREN)
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()
        await chooseChild(/Andreas Jr/)

        // 2 children x 90 days = 180, less the 2 business days requested.
        const row = await screen.findByText(/balance after/i)
        await waitFor(() => expect(row.nextElementSibling?.textContent).toBe('178 / 180'))
    })

    /**
     * The reason this row is not per-child: before a child is picked it still has
     * something true to say. It used to fall through to the annual pool here, and
     * then briefly to a bare 0 — both of which understate what the employee has.
     */
    it('quotes the same total before any child has been picked', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue(TWO_CHILDREN)
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()

        const row = await screen.findByText(/balance after/i)
        await waitFor(() => expect(row.nextElementSibling?.textContent).toBe('178 / 180'))
    })

    /** An aged-out child is worth nothing, so they must not inflate the total. */
    it('leaves an ineligible child out of the total', async () => {
        api.getChildLeaveEntitlements.mockResolvedValue({
            ...TWO_CHILDREN,
            eligibleChildCount: 1,
            totalRemainingDays: 90,
            children: [
                TWO_CHILDREN.children[0],
                { ...TWO_CHILDREN.children[1], isEligible: false, remainingDays: 0, thisYearRemainingDays: 0 },
            ],
        })
        await renderPage()

        fireEvent.click(screen.getByRole('button', { name: /paternity leave/i }))
        pickDates()

        const row = await screen.findByText(/balance after/i)
        await waitFor(() => expect(row.nextElementSibling?.textContent).toBe('88 / 90'))
    })

    it('still quotes the annual pool for an ordinary type', async () => {
        await renderPage()

        // Annual Leave is the default selection: 25 entitlement, 2 days requested.
        pickDates()

        const row = await screen.findByText('Balance after')
        await waitFor(() => expect(row.nextElementSibling?.textContent).toBe('23 / 25'))
    })
})
