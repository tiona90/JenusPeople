import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createAdminUser } from '../../lib/api'
import AdminUsersPanel from './AdminUsersPanel'

// An admin creating a user must not choose a password — the new user sets their
// own from the welcome email — and a display name is now mandatory.
vi.mock('../../lib/api', () => ({
    getAdminUsers: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getDepartments: vi.fn(),
    getUserPresence: vi.fn(),
    getLeaveStatusHistories: vi.fn(),
    getTimesheetStatusHistories: vi.fn(),
    getAnnualLeaves: vi.fn(),
    createAdminUser: vi.fn(),
    updateAdminUser: vi.fn(),
    setAdminUserRoles: vi.fn(),
    confirmAdminUserEmail: vi.fn(),
    deleteAdminUser: vi.fn(),
    updateEmployeeProfile: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

const DEPARTMENT = { id: 7, name: 'Engineering', code: 'ENG', isActive: true }

beforeEach(() => {
    vi.clearAllMocks()

    api.getAdminUsers.mockResolvedValue([])
    api.getEmployeeProfiles.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([DEPARTMENT] as never)
    api.getUserPresence.mockResolvedValue([])
    api.getLeaveStatusHistories.mockResolvedValue([])
    api.getTimesheetStatusHistories.mockResolvedValue([])
    api.getAnnualLeaves.mockResolvedValue([])
})

function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    return render(
        <QueryClientProvider client={queryClient}>
            <AdminUsersPanel />
        </QueryClientProvider>,
    )
}

/** Opens the Create User dialog and returns it. */
async function openCreateDialog() {
    renderPanel()

    const addUser = await screen.findByText('+ Add user')
    fireEvent.click(addUser)

    return screen.getByRole('dialog')
}

describe('AdminUsersPanel — Create User', () => {
    it('offers no password field', async () => {
        const dialog = await openCreateDialog()

        expect(within(dialog).queryByLabelText(/password/i)).not.toBeInTheDocument()
        expect(dialog.querySelectorAll('input[type="password"]')).toHaveLength(0)
    })

    it('explains that the new user sets their own password', async () => {
        const dialog = await openCreateDialog()

        expect(within(dialog).getByText(/secure link to set their own/i)).toBeInTheDocument()
    })

    it('will not submit without a display name', async () => {
        const dialog = await openCreateDialog()

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
        await selectDepartment(dialog)

        expect(within(dialog).getByText('Display name is required')).toBeInTheDocument()
        expect(within(dialog).getByRole('button', { name: /^create$/i })).toBeDisabled()

        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })

        expect(within(dialog).getByRole('button', { name: /^create$/i })).toBeEnabled()
    })

    it('sends the account details without a password', async () => {
        const dialog = await openCreateDialog()

        api.createAdminUser.mockResolvedValue({
            id: 'u1',
            userName: 'newjoiner@example.test',
            email: 'newjoiner@example.test',
            displayName: 'New Joiner',
            imageUrl: '',
            emailConfirmed: true,
            roles: ['Employee'],
            inviteEmailSent: true,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: ' newjoiner@example.test ' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })
        await selectDepartment(dialog)

        fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

        await waitFor(() => expect(createAdminUser).toHaveBeenCalledTimes(1))

        // toEqual on the payload alone: React Query passes its own second
        // argument, and an exact match is what proves no `password` slipped in.
        expect(api.createAdminUser.mock.calls[0][0]).toEqual({
            email: 'newjoiner@example.test',
            displayName: 'New Joiner',
            roles: ['Employee'],
            departmentId: DEPARTMENT.id,
            phoneNumber: null,
            dateOfBirth: null,
        })
    })

    // The account has no password until the invite is used, so a send failure
    // has to reach the admin rather than dying in the log.
    it('reports whether the welcome email went out', async () => {
        const dialog = await openCreateDialog()

        api.createAdminUser.mockResolvedValue({
            id: 'u1',
            userName: 'newjoiner@example.test',
            email: 'newjoiner@example.test',
            displayName: 'New Joiner',
            imageUrl: '',
            emailConfirmed: true,
            roles: ['Employee'],
            inviteEmailSent: false,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })
        await selectDepartment(dialog)
        fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

        expect(await screen.findByText(/the welcome email could not be sent/i)).toBeInTheDocument()
        expect(screen.getByText(/forgot password/i)).toBeInTheDocument()
    })
})

// Presence must come from the check-in state keyed by user id. It was
// previously inferred client-side by substring-matching the company activity
// feed, where the synthetic "Not checked in" row contains "checked in" — so the
// panel badged exactly the people who had *not* checked in as Online.
describe('AdminUsersPanel — presence', () => {
    const USERS = [
        { id: 'u-in', userName: 'in@example.test', email: 'in@example.test', displayName: 'Checked In', imageUrl: '', emailConfirmed: true, roles: ['Employee'] },
        { id: 'u-out', userName: 'out@example.test', email: 'out@example.test', displayName: 'Never In', imageUrl: '', emailConfirmed: true, roles: ['Employee'] },
        { id: 'u-done', userName: 'done@example.test', email: 'done@example.test', displayName: 'Checked Out', imageUrl: '', emailConfirmed: true, roles: ['Employee'] },
        { id: 'u-break', userName: 'break@example.test', email: 'break@example.test', displayName: 'On Break', imageUrl: '', emailConfirmed: true, roles: ['Employee'] },
    ]

    beforeEach(() => {
        api.getAdminUsers.mockResolvedValue(USERS as never)
        api.getUserPresence.mockResolvedValue([
            { userId: 'u-in', status: 'online', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T08:00:00Z' },
            { userId: 'u-out', status: 'offline', checkInAt: null, lastActivityAt: null },
            { userId: 'u-done', status: 'offline', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T17:00:00Z' },
            { userId: 'u-break', status: 'away', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T12:00:00Z' },
        ])
    })

    it('badges one user Online, one Away, and the rest Offline', async () => {
        renderPanel()

        // Four users: only the open check-in is Online, the break is Away, and
        // both "never checked in" and "checked out" are Offline.
        expect(await screen.findByText('Online')).toBeInTheDocument()
        expect(screen.getAllByText('Online')).toHaveLength(1)
        expect(screen.getAllByText('Away')).toHaveLength(1)
        expect(screen.getAllByText('Offline')).toHaveLength(2)
    })

    // The regression itself: never checking in must never read as Online.
    it('shows a user who has never checked in as Offline with no activity', async () => {
        renderPanel()

        await screen.findByText('Never In')
        expect(screen.getByText('No activity')).toBeInTheDocument()
    })

    it('counts and filters only checked-in users as Online', async () => {
        renderPanel()

        const tab = await screen.findByRole('button', { name: /🟢 Online/ })
        expect(within(tab).getByText('1')).toBeInTheDocument()

        fireEvent.click(tab)

        expect(screen.getByText('Checked In')).toBeInTheDocument()
        for (const absent of ['Never In', 'Checked Out', 'On Break']) {
            expect(screen.queryByText(absent)).not.toBeInTheDocument()
        }
    })
})

// A user holds exactly one role, so the role picker must be radios rather than
// checkboxes — checkboxes let an admin tick Manager *and* Employee.
describe('AdminUsersPanel — role selection', () => {
    it('offers the three roles as a single choice, not as checkboxes', async () => {
        const dialog = await openCreateDialog()

        for (const role of ['Admin', 'Manager', 'Employee']) {
            expect(within(dialog).getByRole('radio', { name: role })).toBeInTheDocument()
            expect(within(dialog).queryByRole('checkbox', { name: role })).not.toBeInTheDocument()
        }

        // Employee is the default for a new joiner.
        expect(within(dialog).getByRole('radio', { name: 'Employee' })).toBeChecked()
    })

    it('replaces the current role instead of adding to it', async () => {
        const dialog = await openCreateDialog()

        fireEvent.click(within(dialog).getByRole('radio', { name: 'Manager' }))
        expect(within(dialog).getByRole('radio', { name: 'Manager' })).toBeChecked()
        expect(within(dialog).getByRole('radio', { name: 'Employee' })).not.toBeChecked()

        fireEvent.click(within(dialog).getByRole('radio', { name: 'Admin' }))
        expect(within(dialog).getByRole('radio', { name: 'Admin' })).toBeChecked()
        expect(within(dialog).getByRole('radio', { name: 'Manager' })).not.toBeChecked()

        // Exactly one radio checked at any time.
        const checked = within(dialog).getAllByRole('radio').filter((r) => (r as HTMLInputElement).checked)
        expect(checked).toHaveLength(1)
    })

    it('submits the chosen role as the only role', async () => {
        const dialog = await openCreateDialog()

        api.createAdminUser.mockResolvedValue({
            id: 'u1',
            userName: 'newjoiner@example.test',
            email: 'newjoiner@example.test',
            displayName: 'New Joiner',
            imageUrl: '',
            emailConfirmed: true,
            roles: ['Manager'],
            inviteEmailSent: true,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })
        fireEvent.click(within(dialog).getByRole('radio', { name: 'Manager' }))
        await selectDepartment(dialog)
        fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

        await waitFor(() => expect(createAdminUser).toHaveBeenCalledTimes(1))

        expect(api.createAdminUser.mock.calls[0][0].roles).toEqual(['Manager'])
    })
})

/** MUI's select renders its options into a portal, hence the two steps. */
async function selectDepartment(dialog: HTMLElement) {
    fireEvent.mouseDown(within(dialog).getByRole('combobox'))

    const option = await screen.findByRole('option', { name: `${DEPARTMENT.name} (${DEPARTMENT.code})` })
    fireEvent.click(option)
}
