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
        // Both flags are server-derived from the name (Domain/SystemLeaveTypes.cs),
        // so a fixture must not be free to disagree with its own name — that is how
        // a test ends up asserting against a shape the API never sends. An explicit
        // override still wins, for the cases that want an impossible combination.
        isSystem: overrides.isSystem
            ?? SYSTEM_NAMES.includes(overrides.name ?? 'Paternity Leave'),
        supportsPerChildEntitlement: overrides.supportsPerChildEntitlement
            ?? PER_CHILD_NAMES.includes(overrides.name ?? 'Paternity Leave'),
    }
}

const SYSTEM_NAMES = ['Annual Leave', 'Maternity Leave', 'Paternity Leave']
const PER_CHILD_NAMES = ['Maternity Leave', 'Paternity Leave']

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

/*
 * Per-child entitlement is not a setting an admin chooses -- it is what Maternity
 * and Paternity Leave are. So the three numbers are always visible for those two
 * and there is no switch to reveal them, and every other type gets no section at
 * all rather than a switch it must never turn on. It used to be a toggle on every
 * type, which let an admin put a per-child ledger on, say, Sick Leave -- where a
 * request would then have to name a child.
 */
it.each(['Maternity Leave', 'Paternity Leave'])('always shows the three per-child fields for %s, with no toggle', async (name) => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    expect(screen.getByLabelText(/Total per child/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Max per year, per child/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Eligible until age/)).toBeInTheDocument()

    // The section is labelled, but there is nothing to switch.
    expect(screen.getByText('Per-child entitlement')).toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Per-child entitlement' })).not.toBeInTheDocument()
})

it('shows no per-child section at all for any other leave type', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name: 'Sick Leave', perChildEntitlement: false })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    expect(screen.queryByLabelText(/Total per child/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Max per year, per child/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Eligible until age/)).not.toBeInTheDocument()
    expect(screen.queryByText('Per-child entitlement')).not.toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Per-child entitlement' })).not.toBeInTheDocument()
})

// A per-child type keeps its own ledger and must never also be deducted from the
// pooled balance -- the server refuses that combination, and one day counted in
// both would be charged twice.
it('locks "Affects leave balance" off for a per-child type', async () => {
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    const balanceSwitch = screen.getByRole('switch', { name: 'Affects leave balance' })
    expect(balanceSwitch).not.toBeChecked()
    expect(balanceSwitch).toBeDisabled()
})

it('leaves "Affects leave balance" editable on an ordinary type', async () => {
    api.getLeaveTypes.mockResolvedValue([
        leaveType({ name: 'Sick Leave', perChildEntitlement: false, affectsBalance: true, defaultAllowance: 25 }),
    ])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    const balanceSwitch = screen.getByRole('switch', { name: 'Affects leave balance' })
    expect(balanceSwitch).toBeChecked()
    expect(balanceSwitch).toBeEnabled()
})

/*
 * The server refuses a 0 total, cap or age on a per-child type, so a stored 0 must
 * not reach the field -- the dialog would open already invalid, with the reason
 * shown only after a save. Reachable on any database configured before this
 * section became unconditional: Maternity Leave is seeded with the column at 0.
 */
it('falls back to a valid default rather than opening on a stored 0', async () => {
    api.getLeaveTypes.mockResolvedValue([
        leaveType({
            name: 'Maternity Leave',
            perChildTotalWeeks: 0,
            perChildWeeksPerYear: 0,
            childEligibleUntilAge: 0,
        }),
    ])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    expect(screen.getByLabelText(/Total per child/)).toHaveValue(18)
    expect(screen.getByLabelText(/Max per year, per child/)).toHaveValue(5)
    expect(screen.getByLabelText(/Eligible until age/)).toHaveValue(15)
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

/*
 * Annual, Maternity and Paternity Leave are seeded and found by name elsewhere, so
 * they cannot be renamed or deleted. The panel follows the server's `isSystem`
 * flag rather than matching names itself -- it used to hard-code 'annual leave',
 * which protected only that one type and only in the UI.
 */
it('makes a built-in leave type\'s name read-only, and says why', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ isSystem: true })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    // Read-only rather than disabled, so the name still reads as a real value and
    // is still submitted back unchanged.
    const nameField = screen.getByLabelText(/^Name/)
    expect(nameField).toHaveAttribute('readonly')
    expect(nameField).not.toBeDisabled()
    expect(screen.getByText('Built-in leave type — the name cannot be changed.')).toBeInTheDocument()
})

it('leaves a custom leave type\'s name editable', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name: 'Study Leave', isSystem: false })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    const nameField = screen.getByLabelText(/^Name/)
    expect(nameField).not.toHaveAttribute('readonly')
    fireEvent.change(nameField, { target: { value: 'Training Leave' } })
    expect(nameField).toHaveValue('Training Leave')
})

it('offers no delete button for a built-in leave type', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ isSystem: true })])
    await renderPanel()

    expect(screen.queryByTitle('Delete')).not.toBeInTheDocument()
    // Editing it is still offered -- only the name and deletion are off limits.
    expect(screen.getByTitle('Edit')).toBeInTheDocument()
})

it('still offers delete for a custom leave type', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name: 'Study Leave', isSystem: false })])
    await renderPanel()

    expect(screen.getByTitle('Delete')).toBeInTheDocument()
})

/*
 * Being built in is not the same as being undisableable. Annual leave alone cannot
 * be switched off, because it is the type the enforced balance is a budget for.
 * Maternity and Paternity are protected from renaming and deletion but an
 * organisation that does not offer them must still be able to hide them.
 */
it('keeps annual leave from being switched off', async () => {
    api.getLeaveTypes.mockResolvedValue([
        leaveType({ name: 'Annual Leave', isSystem: true, affectsBalance: true, perChildEntitlement: false, defaultAllowance: 25 }),
    ])
    await renderPanel()

    expect(screen.getAllByRole('switch')[0]).toBeDisabled()
})

it('lets the other built-in types be switched off', async () => {
    for (const name of ['Maternity Leave', 'Paternity Leave']) {
        api.getLeaveTypes.mockResolvedValue([leaveType({ name, isSystem: true })])
        const view = await renderPanel()

        expect(screen.getAllByRole('switch')[0]).toBeEnabled()
        view.unmount()
    }
})

/*
 * A flat "default allowance" is meaningless on Maternity and Paternity Leave: their
 * budget is per child, expressed by the three numbers above, and the card has always
 * quoted those instead. Leaving the input on screen invited an admin to set a number
 * nothing reads — and Maternity Leave ships with a stored 90 doing exactly that.
 *
 * So the field is gone for those two and the payload carries 0, including from the
 * Enabled toggle, which resubmits the whole type and would otherwise write the stale
 * 90 straight back.
 */
it.each(['Maternity Leave', 'Paternity Leave'])('offers no flat allowance field for %s', async (name) => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name, defaultAllowance: 90 })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    expect(screen.queryByLabelText('Default allowance')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Unit')).not.toBeInTheDocument()
})

it('still offers the allowance field for a type whose budget really is a flat one', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name: 'Sick Leave', perChildEntitlement: false, defaultAllowance: 10 })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))

    expect(screen.getByLabelText('Default allowance')).toBeInTheDocument()
})

it('clears the stored allowance when a parental type is saved', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name: 'Maternity Leave', defaultAllowance: 90 })])
    await renderPanel()

    fireEvent.click(screen.getByTitle('Edit'))
    fireEvent.click(screen.getByRole('button', { name: /save/i }))

    await waitFor(() => expect(api.updateLeaveType).toHaveBeenCalledTimes(1))
    expect(api.updateLeaveType.mock.calls[0][1]).toMatchObject({ defaultAllowance: 0 })
})

/* The trap CLAUDE.md warns about: this toggle resubmits the whole leave type. */
it('does not write the stale allowance back when a parental type is toggled', async () => {
    api.getLeaveTypes.mockResolvedValue([leaveType({ name: 'Maternity Leave', defaultAllowance: 90 })])
    await renderPanel()

    // The card's Enabled switch carries no accessible name, and it is the only
    // switch on screen while no dialog is open.
    fireEvent.click(screen.getByRole('switch'))

    await waitFor(() => expect(api.updateLeaveType).toHaveBeenCalledTimes(1))
    expect(api.updateLeaveType.mock.calls[0][1]).toMatchObject({ defaultAllowance: 0 })
})
