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

function renderPicker(overrides: Partial<React.ComponentProps<typeof ChildLeavePicker>> = {}) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={client}>
            <ChildLeavePicker value="" onChange={vi.fn()} requestedDays={null} {...overrides} />
        </QueryClientProvider>,
    )
}

describe('ChildLeavePicker', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        getChildLeaveEntitlements.mockResolvedValue(summary)
    })

    /**
     * A MUI Select opens on mouseDown, not click, and renders its options into a
     * portal — screen queries still reach them, but only once it is open. The
     * combobox exists (disabled) as soon as the component mounts, before the
     * entitlement query settles, so wait for it to become enabled first — firing
     * mouseDown on a still-disabled Select is a silent no-op and the listbox
     * would never open.
     */
    async function openPicker() {
        const combobox = await screen.findByRole('combobox', { name: /child/i })
        await waitFor(() => expect(combobox).not.toHaveAttribute('aria-disabled', 'true'))
        fireEvent.mouseDown(combobox)
        return screen.findByRole('listbox')
    }

    it('quotes each eligible child their own remaining entitlement', async () => {
        renderPicker()
        await openPicker()

        expect(screen.getByText(/Andreas/)).toBeInTheDocument()
        expect(screen.getByText(/65 of 90 days left/)).toBeInTheDocument()
        expect(screen.getByText(/25 left this year/)).toBeInTheDocument()
    })

    /**
     * Shown disabled with the reason rather than hidden: an employee who cannot
     * find their child assumes the data is missing, not that the child aged out.
     */
    it('shows an aged-out child disabled, with the reason', async () => {
        renderPicker()
        await openPicker()

        const agedOut = screen.getByText(/Petros/)
        expect(agedOut).toBeInTheDocument()
        expect(screen.getByText(/no longer eligible/i)).toBeInTheDocument()
        // aria-disabled rather than the disabled attribute: MUI marks a disabled
        // MenuItem for assistive tech and keeps it in the list.
        expect(agedOut.closest('li')).toHaveAttribute('aria-disabled', 'true')
    })

    it('says where to add children when none are declared', async () => {
        getChildLeaveEntitlements.mockResolvedValue({ ...summary, eligibleChildCount: 0, children: [] })

        renderPicker()

        expect(await screen.findByText(/add your children in edit profile/i)).toBeInTheDocument()
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

    it('shows what the chosen dates would leave', async () => {
        renderPicker({ value: 'c1', requestedDays: 6 })

        expect(await screen.findByText(/6 business days \(1\.2 weeks\)/)).toBeInTheDocument()
        expect(screen.getByText(/59 days left/)).toBeInTheDocument()
    })
})
