import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChildLeavePicker from './ChildLeavePicker'
import type { ChildLeaveEntitlementSummary } from '../../lib/types'

const getChildLeaveEntitlements = vi.fn()

vi.mock('../../lib/api', () => ({
    getChildLeaveEntitlements: (...args: unknown[]) => getChildLeaveEntitlements(...args),
}))

const summary: ChildLeaveEntitlementSummary = {
    leaveTypeId: 1,
    leaveTypeName: 'Paternity Leave',
    eligibleChildCount: 1,
    totalRemainingDays: 65,
    thisYearCapDays: 25,
    thisYearRemainingDays: 25,
    children: [
        {
            childId: 'c1', name: 'Andreas', dateOfBirth: '2019-03-04', ageYears: 7,
            isEligible: true, lastEligibleDate: '2034-03-03',
            totalDays: 90, totalWeeks: 18, usedDays: 25, remainingDays: 65,
            thisYearCapDays: 25, thisYearUsedDays: 0, thisYearRemainingDays: 25,
            leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
        },
        {
            childId: 'c2', name: 'Petros', dateOfBirth: '2005-01-20', ageYears: 21,
            isEligible: false, lastEligibleDate: '2020-01-19',
            totalDays: 0, totalWeeks: 0, usedDays: 40, remainingDays: 0,
            thisYearCapDays: 0, thisYearUsedDays: 0, thisYearRemainingDays: 0,
            leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
        },
    ],
}

/**
 * Two eligible children, so there is a real choice. The default fixture has only
 * one, which the picker now makes for the employee.
 */
const twoEligible: ChildLeaveEntitlementSummary = {
    ...summary,
    eligibleChildCount: 2,
    children: [
        summary.children[0],
        // Ages out five years before Andreas, so she is the one the picker should
        // charge the request to — the entitlement that expires first.
        { ...summary.children[0], childId: 'c3', name: 'Sofia', lastEligibleDate: '2029-06-30' },
        summary.children[1],
    ],
}

function renderPicker(overrides: Partial<React.ComponentProps<typeof ChildLeavePicker>> = {}) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={client}>
            <ChildLeavePicker
                value=""
                onChange={vi.fn()}
                childEligibleUntilAge={15}
                requestedDays={null}
                {...overrides}
            />
        </QueryClientProvider>,
    )
}

describe('ChildLeavePicker', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        getChildLeaveEntitlements.mockResolvedValue(summary)
    })

    /**
     * The children are a grid of cards, not a dropdown — there is nothing to open,
     * and every child's figures are readable at once. They appear when the
     * entitlement query settles, so wait for the grid rather than asserting into
     * an empty one.
     */
    function waitForGrid() {
        return screen.findByText(/Andreas/)
    }

    /**
     * The card element for a child: the nearest ancestor of the name that also
     * carries the availability line, which is the card and not one of the rows
     * inside it.
     */
    function cardFor(name: string) {
        let element: HTMLElement | null = screen.getByText(name)
        while (element && !element.textContent?.includes('Available until')) {
            element = element.parentElement
        }
        if (!element) throw new Error(`No card found for ${name}`)
        return element
    }


    it('quotes each eligible child their own remaining entitlement', async () => {
        renderPicker()
        await waitForGrid()

        expect(screen.getByText(/Andreas/)).toBeInTheDocument()
        expect(screen.getByText(/65 of 90 days left/)).toBeInTheDocument()
        expect(screen.getByText(/25 left this year/)).toBeInTheDocument()
    })

    /**
     * Shown with the reason rather than hidden: an employee who cannot find their
     * child assumes the data is missing, not that the child aged out.
     *
     * Not a disabled button — not a button at all. A disabled button is still a
     * button: it keeps the shape and the affordance of something to press, and
     * reads as broken rather than as inapplicable. There is nothing here to click,
     * nothing to tab onto, and no pointer cursor inviting a try.
     */
    it('shows an aged-out child as plain text, not something to click', async () => {
        renderPicker()
        await waitForGrid()

        expect(screen.getByText(/Petros/)).toBeInTheDocument()
        expect(screen.getByText(/no longer eligible/i)).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /Petros/ })).not.toBeInTheDocument()
    })

    /**
     * Fix 5: the label used to quote the child's *current* age against the day
     * *before* the qualifying birthday — two errors in one phrase. Petros was born
     * 20 Jan 2005 and is 21 now; with a cut-off of 15 he turned 15 on 20 Jan 2020,
     * the day after his last eligible date. The old label read "turned 21 on
     * 19 Jan 2020". The server's own refusal message states it the new way.
     */
    it('names the cut-off age and the birthday it was reached on, not today\'s age', async () => {
        renderPicker()
        await waitForGrid()

        expect(screen.getByText(/turned 15 on 20 Jan 2020/i)).toBeInTheDocument()
        expect(screen.queryByText(/turned 21/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/19 Jan 2020/)).not.toBeInTheDocument()
    })

    /**
     * The grid replaced a dropdown. Every child's figures are on screen at once,
     * so choosing between them no longer means opening a select and reading the
     * options one at a time.
     */
    it('shows every child at once, with no dropdown to open', async () => {
        renderPicker()
        await waitForGrid()

        // Petros is on the grid too, as plain text — he has aged out.
        expect(screen.getByText(/Petros/)).toBeInTheDocument()
        expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    })

    /**
     * No asterisk. It was there from the dropdown, where a required select needs
     * marking; a grid where one card must be chosen before submit unlocks says it
     * more plainly than a symbol does, and the submit button names what is missing.
     */
    it('labels the grid without a required asterisk', async () => {
        renderPicker()
        await waitForGrid()

        // Exact text, so a lone "*" alongside it would fail rather than be
        // swallowed by a substring match.
        expect(screen.getByText('Child').textContent).toBe('Child')
    })

    /**
     * A choice with one answer is not a choice. With a single eligible child the
     * picker selects them and the cards become read-only — nothing to click, and
     * no step that can only be completed one way.
     */
    it('selects the only eligible child without being asked', async () => {
        const onChange = vi.fn()
        renderPicker({ onChange })

        await waitForGrid()
        await waitFor(() => expect(onChange).toHaveBeenCalledWith('c1'))
    })

    it('renders nothing clickable when there is only one eligible child', async () => {
        renderPicker({ value: 'c1' })
        await waitForGrid()

        expect(screen.queryByRole('button')).not.toBeInTheDocument()
    })

    /** No card is ever a button, however many children qualify. */
    it('renders nothing clickable even when several children are eligible', async () => {
        getChildLeaveEntitlements.mockResolvedValue(twoEligible)
        renderPicker()
        await waitForGrid()

        expect(screen.queryByRole('button')).not.toBeInTheDocument()
    })

    /**
     * With the cards read-only, the picker has to name a child itself — the
     * server requires one on a per-child request. It takes the eligible child
     * whose entitlement runs out soonest, so an approaching birthday does not
     * quietly strip days nobody got round to using. Sofia ages out first here.
     */
    it('charges the request to the child whose entitlement expires soonest', async () => {
        getChildLeaveEntitlements.mockResolvedValue(twoEligible)
        const onChange = vi.fn()
        renderPicker({ onChange })

        await waitForGrid()
        await waitFor(() => expect(onChange).toHaveBeenCalledWith('c3'))
    })

    /**
     * Every eligible child looks the same. The card the request is charged to
     * used to be picked out — first by a green tint, then by a firmer border —
     * and both read as "you selected this" on a grid where nothing can be
     * selected. Emotion gives identical styles the same class, so comparing the
     * two catches any styling that creeps back in.
     */
    it('draws every eligible child identically, marking none as chosen', async () => {
        getChildLeaveEntitlements.mockResolvedValue(twoEligible)
        renderPicker({ value: 'c1' })
        await waitForGrid()

        const chosen = cardFor('Andreas')
        const other = cardFor('Sofia')

        expect(chosen.className).toBe(other.className)
    })

    /**
     * Which child the request is charged to is still announced to assistive
     * tech, since it is a real fact about the request — it simply is not drawn.
     */
    it('marks the chosen card without a tick', async () => {
        getChildLeaveEntitlements.mockResolvedValue(twoEligible)
        renderPicker({ value: 'c1' })
        await waitForGrid()

        const card = screen.getByText('Andreas').closest('[aria-current]')
        expect(card).toHaveAttribute('aria-current', 'true')
        expect(card?.textContent).not.toContain('✓')
    })

    it('leaves the children it did not choose unmarked', async () => {
        getChildLeaveEntitlements.mockResolvedValue(twoEligible)
        renderPicker({ value: 'c3' })
        await waitForGrid()

        expect(screen.getByText('Andreas').closest('[aria-current]')).toBeNull()
    })

    /** The dates half of the card: how long this child goes on qualifying. */
    it('shows how long each eligible child stays eligible', async () => {
        renderPicker()

        expect(await screen.findByText(/Available until 03 Mar 2034/)).toBeInTheDocument()
    })

    it('does nothing when an aged-out child is clicked', async () => {
        const onChange = vi.fn()
        renderPicker({ onChange })
        await waitForGrid()

        fireEvent.click(screen.getByText(/Petros/))

        // Not "never called": with one eligible child the picker selects them for
        // the employee. What must never happen is the aged-out child winning.
        expect(onChange).not.toHaveBeenCalledWith('c2')
    })

    it('says where to add children when none are declared', async () => {
        getChildLeaveEntitlements.mockResolvedValue({ ...summary, eligibleChildCount: 0, children: [] })

        renderPicker()

        expect(await screen.findByText(/add your children in edit profile/i)).toBeInTheDocument()
    })

    /**
     * Fix 7: an admin filing on someone else's behalf was told to "add your
     * children in Edit profile" — nonsense, since there is deliberately no screen
     * for editing another employee's children.
     */
    it('names the employee instead of saying "your children" when filing for someone else', async () => {
        getChildLeaveEntitlements.mockResolvedValue({ ...summary, eligibleChildCount: 0, children: [] })

        renderPicker({ employeeId: 'emp-42', onBehalfOfName: 'Maria Ioannou' })

        expect(await screen.findByText(/Maria Ioannou has no children on file/i)).toBeInTheDocument()
        expect(screen.queryByText(/add your children/i)).not.toBeInTheDocument()
    })

    it('explains when children exist but none are eligible', async () => {
        getChildLeaveEntitlements.mockResolvedValue({
            ...summary,
            eligibleChildCount: 0,
            children: [summary.children[1]],
        })

        renderPicker()

        expect(await screen.findByText(/no eligible children/i)).toBeInTheDocument()
    })

    /**
     * Fix 6: the message hard-coded "under 15" in a feature whose entire premise is
     * that the age is configurable on the leave type.
     */
    it('quotes the configured cut-off age, not a hard-coded 15', async () => {
        getChildLeaveEntitlements.mockResolvedValue({
            ...summary,
            eligibleChildCount: 0,
            children: [summary.children[1]],
        })

        renderPicker({ childEligibleUntilAge: 18 })

        expect(await screen.findByText(/only while a child is under 18/i)).toBeInTheDocument()
    })

    it('shows what the chosen dates would leave, capped by the tighter of the two remainders', async () => {
        // c1 has 65 lifetime days left but only 25 this year; the request must
        // be weighed against 25, not 65, or the caption over-promises what the
        // yearly cap will actually allow.
        renderPicker({ value: 'c1', requestedDays: 6 })

        expect(await screen.findByText(/6 business days \(1\.2 weeks\)/)).toBeInTheDocument()
        expect(screen.getByText(/19 days left/)).toBeInTheDocument()
    })

    /**
     * Fix 1: the picker must be asked for the *right* person's ledger. This is
     * the test that would have caught the picker silently loading the caller's
     * own children while an admin edited someone else's request — it asserts on
     * the argument passed to the query function, not just on what renders.
     */
    it('requests the given employee\'s entitlements, not the caller\'s own', async () => {
        renderPicker({ employeeId: 'emp-42' })

        // Second argument is the leave type whose ledger to read; this picker is
        // rendered without one, so the server resolves it.
        await waitFor(() => expect(getChildLeaveEntitlements).toHaveBeenCalledWith('emp-42', undefined))
    })

    it('requests the caller\'s own entitlements when no employeeId is given', async () => {
        renderPicker()

        await waitFor(() => expect(getChildLeaveEntitlements).toHaveBeenCalledWith(undefined, undefined))
    })

    /**
     * Fix 2: once the ledger for the *current* employeeId has loaded and the
     * held value matches none of its children (e.g. an admin switched employee
     * after picking a child for the previous one), the stale id must be cleared
     * rather than silently submitted while the select displays blank.
     */
    it('clears a value that matches no loaded child once the query settles', async () => {
        const onChange = vi.fn()
        renderPicker({ value: 'stale-child-id', onChange })

        await waitFor(() => expect(onChange).toHaveBeenCalledWith(''))
    })

    it('does not clear a value that still matches a loaded child', async () => {
        const onChange = vi.fn()
        renderPicker({ value: 'c1', onChange })

        await screen.findByText(/Andreas/)
        expect(onChange).not.toHaveBeenCalled()
    })

    /**
     * Fix 4: a failed query must not be indistinguishable from "no children
     * declared" — the latter tells an employee to add their own children, which
     * is nonsense (and, for an admin acting on someone else's request, actively
     * misleading) when the real problem is that the request failed.
     */
    it('shows a failure message, not the add-your-children one, when the query errors', async () => {
        getChildLeaveEntitlements.mockReset()
        getChildLeaveEntitlements.mockRejectedValue(new Error('network down'))

        renderPicker()

        expect(await screen.findByText(/network down/i)).toBeInTheDocument()
        expect(screen.queryByText(/add your children in edit profile/i)).not.toBeInTheDocument()
    })
})
