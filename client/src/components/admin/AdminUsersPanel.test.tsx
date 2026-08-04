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
    getCompanyAttendance: vi.fn(),
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
    api.getCompanyAttendance.mockResolvedValue(null as never)
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

/** MUI's select renders its options into a portal, hence the two steps. */
async function selectDepartment(dialog: HTMLElement) {
    fireEvent.mouseDown(within(dialog).getByRole('combobox'))

    const option = await screen.findByRole('option', { name: `${DEPARTMENT.name} (${DEPARTMENT.code})` })
    fireEvent.click(option)
}
