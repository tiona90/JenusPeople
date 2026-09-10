import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, expect, it, vi } from 'vitest'
import { StoreProvider } from '../../lib/mobx'
import type { LeaveType } from '../../lib/types'
import LeaveTypesPanel from './LeaveTypesPanel'

// Leave types configure the org-wide rules (approval, attachment, allowance) and,
// for paternity leave, a separate per-child ledger. What matters here is that the
// three per-child numbers only reveal once their own toggle is on, that turning
// the toggle on clears and disables "Affects leave balance" (the server refuses
// that combination outright), and that a per-child type's card quotes the policy
// instead of the meaningless 0 `defaultAllowance` migration leaves behind.
vi.mock('../../lib/api', () => ({
    getLeaveTypes: vi.fn(),
    getAnnualLeaves: vi.fn(),
    createLeaveType: vi.fn(),
    updateLeaveType: vi.fn(),
    deleteLeaveType: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

function leaveType(overrides: Partial<LeaveType> = {}): LeaveType {
    return {
        id: 1,
        name: 'Paternity Leave',
        requiresApproval: true,
        isActive: true,
        affectsBalance: false,
        icon: '👶',
        colorKey: 'paternity',
        description: '',
        paid: true,
        attachmentPolicy: 'None',
        defaultAllowance: 0,
        allowanceUnit: 'days/year',
        maxCarryoverDays: 0,
        perChildEntitlement: true,
        perChildTotalWeeks: 18,
        perChildWeeksPerYear: 5,
        childEligibleUntilAge: 15,
        accrualNotes: '',
        minNoticeDays: 0,
        maxConsecutiveDays: 0,
        halfDayAllowed: false,
        eligibilityNotes: 'All employees',
        eligibilityScope: 'All',
        ...overrides,
    }
}

const PATERNITY = leaveType()

beforeEach(() => {
    vi.clearAllMocks()
    api.getLeaveTypes.mockResolvedValue([PATERNITY])
    api.getAnnualLeaves.mockResolvedValue([])
    api.createLeaveType.mockResolvedValue(PATERNITY)
    api.updateLeaveType.mockResolvedValue(PATERNITY)
})

async function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <LeaveTypesPanel />
            </QueryClientProvider>
        </StoreProvider>,
    )
    await screen.findByPlaceholderText('Search leave types…')
    return view
}

it('quotes the per-child policy on the card instead of the meaningless 0 allowance', async () => {
    await renderPanel()

    expect(screen.getByText('18 weeks per child · max 5 weeks/year')).toBeInTheDocument()
})

it('reveals the three per-child fields only while the toggle is on', async () => {
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    // PATERNITY already has the toggle on, so the fields show straight away.
    expect(screen.getByLabelText(/Total per child/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Max per year, per child/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Eligible until age/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('switch', { name: 'Per-child entitlement' }))

    expect(screen.queryByLabelText(/Total per child/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Max per year, per child/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Eligible until age/)).not.toBeInTheDocument()
})

it('clears and disables "Affects leave balance" the moment per-child entitlement is turned on', async () => {
    // Start from a type that both affects the balance and has the per-child toggle
    // off -- turning the toggle on must flip and lock the other switch, since the
    // server refuses a per-child type that also affects the pooled balance.
    api.getLeaveTypes.mockResolvedValue([
        leaveType({ perChildEntitlement: false, affectsBalance: true, defaultAllowance: 25 }),
    ])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))
    const balanceSwitch = screen.getByRole('switch', { name: 'Affects leave balance' })
    expect(balanceSwitch).toBeChecked()
    expect(balanceSwitch).toBeEnabled()

    fireEvent.click(screen.getByRole('switch', { name: 'Per-child entitlement' }))

    expect(balanceSwitch).not.toBeChecked()
    expect(balanceSwitch).toBeDisabled()
})

it('sends the per-child fields in the update payload', async () => {
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateLeaveType).toHaveBeenCalledWith(PATERNITY.id, expect.objectContaining({
        perChildEntitlement: true,
        perChildTotalWeeks: 18,
        perChildWeeksPerYear: 5,
        childEligibleUntilAge: 15,
        affectsBalance: false,
    })))
})

/**
 * The card's Enabled/Disabled switch is a shortcut for an edit, and the endpoint
 * replaces the leave type rather than patching it (see `toggleActive` in
 * LeaveTypesPanel.tsx) -- so a per-child type's 18/5/15 configuration has to be
 * sent back unchanged, or flipping the switch would silently zero it. A zeroed
 * `perChildTotalWeeks` refuses every request for that type (the server's
 * per-child balance calculator has nothing to grant), so this would read as
 * paternity leave quietly breaking with no error anywhere -- not as a crash this
 * suite would otherwise catch.
 */
it('sends a per-child type\'s policy unchanged when toggling it off from the card', async () => {
    await renderPanel()

    // The panel starts with no dialog open, so the card's own switch is the only
    // one on screen.
    fireEvent.click(screen.getAllByRole('switch')[0])

    await waitFor(() => expect(api.updateLeaveType).toHaveBeenCalledWith(PATERNITY.id, expect.objectContaining({
        isActive: false,
        perChildEntitlement: true,
        perChildTotalWeeks: 18,
        perChildWeeksPerYear: 5,
        childEligibleUntilAge: 15,
    })))
})
