import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createAdminUser } from '../../lib/api'
import AdminUsersPanel from './AdminUsersPanel'

// An admin creating a user must not choose a password — the new user sets their
// own from the welcome email — and a display name is now mandatory.
vi.mock('../../lib/api', () => ({
    getAdminUsers: vi.fn(),
    getAppSettings: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getDepartments: vi.fn(),
    getLeaveTypes: vi.fn(),
    getUserPresence: vi.fn(),
    getLeaveStatusHistories: vi.fn(),
    getTimesheetStatusHistories: vi.fn(),
    getAnnualLeaves: vi.fn(),
    createAdminUser: vi.fn(),
    updateAdminUser: vi.fn(),
    setAdminUserRoles: vi.fn(),
    confirmAdminUserEmail: vi.fn(),
    setAdminUserActive: vi.fn(),
    deleteAdminUser: vi.fn(),
    updateEmployeeProfile: vi.fn(),
}))

// Only SweetAlert is stubbed: sweetalert2 renders a real modal and resolves on a
// click nobody makes in jsdom, so an unmocked confirm hangs the test. Everything
// else in ../ui (AppDialog and friends) stays real, because the dialogs under
// test are built from it. vi.hoisted, because vi.mock's factory is lifted above
// ordinary declarations and would not see a plain const.
const { sweetAlertFire } = vi.hoisted(() => ({ sweetAlertFire: vi.fn() }))

vi.mock('../ui', async (importOriginal) => ({
    ...(await importOriginal<typeof import('../ui')>()),
    SweetAlert: { fire: sweetAlertFire },
}))

const api = vi.mocked(await import('../../lib/api'))

const DEPARTMENT = { id: 7, name: 'Engineering', code: 'ENG', isActive: true }

/* The annual-leave allowance an employee's entitlement is measured against comes from
   Leave Types (25 days/year as seeded), not from a number hard-coded in the panel. */
const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', isActive: true, affectsBalance: true, defaultAllowance: 25,
    allowanceUnit: 'days/year',
}

beforeEach(() => {
    vi.clearAllMocks()

    api.getAdminUsers.mockResolvedValue([])
    api.getEmployeeProfiles.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([DEPARTMENT] as never)
    api.getUserPresence.mockResolvedValue([])
    api.getLeaveStatusHistories.mockResolvedValue([])
    api.getTimesheetStatusHistories.mockResolvedValue([])
    api.getAnnualLeaves.mockResolvedValue([])
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE] as never)
    api.getAppSettings.mockResolvedValue({ defaultAnnualEntitlement: 20 } as never)
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
            isActive: true,
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
            managerId: null,
            jobTitle: null,
            // The annual-leave allowance from Leave Types, not a number typed into
            // the panel's source.
            annualLeaveEntitlement: ANNUAL_LEAVE_TYPE.defaultAllowance,
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
            isActive: true,
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
        { id: 'u-break', userName: 'break@example.test', email: 'break@example.test', displayName: 'Break User', imageUrl: '', emailConfirmed: true, roles: ['Employee'] },
    ]

    beforeEach(() => {
        api.getAdminUsers.mockResolvedValue(USERS as never)
        api.getUserPresence.mockResolvedValue([
            { userId: 'u-in', status: 'online', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T08:00:00Z', isAutoBreak: false },
            { userId: 'u-out', status: 'offline', checkInAt: null, lastActivityAt: null, isAutoBreak: false },
            { userId: 'u-done', status: 'offline', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T17:00:00Z', isAutoBreak: false },
            { userId: 'u-break', status: 'away', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T12:00:00Z', isAutoBreak: false },
        ])
    })

    it('badges one user Online, one On Break, and the rest Offline', async () => {
        renderPanel()

        // Four users: only the open check-in is Online, the break is On Break, and
        // both "never checked in" and "checked out" are Offline.
        expect(await screen.findByText('Online')).toBeInTheDocument()
        expect(screen.getAllByText('Online')).toHaveLength(1)
        expect(screen.getAllByText('On Break')).toHaveLength(1)
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
        for (const absent of ['Never In', 'Checked Out', 'Break User']) {
            expect(screen.queryByText(absent)).not.toBeInTheDocument()
        }
    })
})

// A user's manager is whoever manages their department, not a free pick — an
// admin could previously assign anyone (even across departments) as the person
// this user reports to, which drifted from what the Departments page showed as
// that department's manager.
describe('AdminUsersPanel — manager is derived from department', () => {
    const MANAGER_USER = { id: 'u-manager', userName: 'manager@example.test', email: 'manager@example.test', displayName: 'Andreas Georgiou', imageUrl: '', emailConfirmed: true, roles: ['Manager'] }
    const EMPLOYEE_USER = { id: 'u-employee', userName: 'employee@example.test', email: 'employee@example.test', displayName: 'Theodoros Iona', imageUrl: '', emailConfirmed: true, roles: ['Employee'] }

    const MANAGER_PROFILE = { id: 'p-manager', userId: 'u-manager', displayName: 'Andreas Georgiou', departmentId: DEPARTMENT.id, managerId: null, annualLeaveEntitlement: 20, leaveBalance: 20, jobTitle: null, createdAt: '2026-01-01' }
    const EMPLOYEE_PROFILE = { id: 'p-employee', userId: 'u-employee', displayName: 'Theodoros Iona', departmentId: DEPARTMENT.id, managerId: null, annualLeaveEntitlement: 20, leaveBalance: 20, jobTitle: null, createdAt: '2026-01-01' }

    beforeEach(() => {
        api.getAdminUsers.mockResolvedValue([MANAGER_USER, EMPLOYEE_USER] as never)
        api.getEmployeeProfiles.mockResolvedValue([MANAGER_PROFILE, EMPLOYEE_PROFILE] as never)
    })

    async function openEditFor(displayName: string) {
        renderPanel()
        const nameEl = await screen.findByText(displayName)
        // Four ancestors up from the name text: its own box -> the "User" cell
        // -> the row's grid -> the row container that also holds the action buttons.
        const row = nameEl.parentElement!.parentElement!.parentElement!.parentElement!
        fireEvent.click(within(row).getByTitle('Edit'))
        return screen.getByRole('dialog')
    }

    it('shows the department manager as a fixed value, not a pickable dropdown', async () => {
        const dialog = await openEditFor('Theodoros Iona')

        expect(await within(dialog).findByDisplayValue('Andreas Georgiou')).toBeInTheDocument()
        expect(within(dialog).queryByRole('combobox', { name: /manager/i })).not.toBeInTheDocument()
        // The Manager field is read-only rather than disabled, so the derived name
        // renders as a filled-in value instead of greyed-out placeholder-looking
        // text. getByLabelText would also match the "Manager" role radio, so target
        // the textbox role specifically.
        const managerField = within(dialog).getByRole('textbox', { name: 'Manager' })
        expect(managerField).toHaveAttribute('readonly')
        expect(managerField).not.toBeDisabled()
    })

    it('sends the department manager\'s profile id on save without letting it be edited', async () => {
        const dialog = await openEditFor('Theodoros Iona')

        // Wait for the department-driven effect to populate the form before
        // saving — otherwise the click races the microtask that sets it.
        await within(dialog).findByDisplayValue('Andreas Georgiou')
        fireEvent.click(within(dialog).getByRole('button', { name: /^save$/i }))

        await waitFor(() => expect(api.updateEmployeeProfile).toHaveBeenCalledTimes(1))
        expect(api.updateEmployeeProfile.mock.calls[0][0]).toMatchObject({
            id: EMPLOYEE_PROFILE.id,
            managerId: MANAGER_PROFILE.id,
        })
    })

    // A manager *is* the person the field would name, so they get no Manager field
    // at all — and never themself, which is what this used to guard against.
    it('offers no manager for a manager\'s own record, and saves none', async () => {
        const dialog = await openEditFor('Andreas Georgiou')

        // Wait for the role-driven effect to settle before reading the form.
        await waitFor(() => expect(within(dialog).getByRole('radio', { name: 'Manager' })).toBeChecked())

        expect(within(dialog).queryByRole('textbox', { name: 'Manager' })).not.toBeInTheDocument()
        expect(within(dialog).queryByText(/set by the department's manager/i)).not.toBeInTheDocument()
        // The rest of the Profile section stays.
        expect(within(dialog).getByText('Profile')).toBeInTheDocument()

        fireEvent.click(within(dialog).getByRole('button', { name: /^save$/i }))

        await waitFor(() => expect(api.updateEmployeeProfile).toHaveBeenCalledTimes(1))
        expect(api.updateEmployeeProfile.mock.calls[0][0]).toMatchObject({
            id: MANAGER_PROFILE.id,
            managerId: null,
        })
    })

    // The hidden field must not rewrite what is stored: a manager who already has
    // a managerId keeps it on save rather than having it cleared or re-derived.
    it('leaves a manager\'s stored managerId untouched when the field is hidden', async () => {
        api.getEmployeeProfiles.mockResolvedValue([
            { ...MANAGER_PROFILE, managerId: 'p-someone-else' },
            EMPLOYEE_PROFILE,
        ] as never)

        const dialog = await openEditFor('Andreas Georgiou')
        await waitFor(() => expect(within(dialog).getByRole('radio', { name: 'Manager' })).toBeChecked())

        fireEvent.click(within(dialog).getByRole('button', { name: /^save$/i }))

        await waitFor(() => expect(api.updateEmployeeProfile).toHaveBeenCalledTimes(1))
        expect(api.updateEmployeeProfile.mock.calls[0][0]).toMatchObject({
            id: MANAGER_PROFILE.id,
            managerId: 'p-someone-else',
        })
    })

    // Create User must land a new hire on the same manager Edit would show for
    // that department — the two forms should never disagree about who manages whom.
    it('Create User picks up the department manager too, once a department is chosen', async () => {
        const dialog = await openCreateDialog()

        api.createAdminUser.mockResolvedValue({
            id: 'u-new',
            userName: 'newhire@example.test',
            email: 'newhire@example.test',
            displayName: 'New Hire',
            imageUrl: '',
            emailConfirmed: true,
            isActive: true,
            roles: ['Employee'],
            inviteEmailSent: true,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newhire@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Hire' } })
        await selectDepartment(dialog)

        expect(await within(dialog).findByDisplayValue('Andreas Georgiou')).toBeInTheDocument()

        fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

        await waitFor(() => expect(api.createAdminUser).toHaveBeenCalledTimes(1))
        expect(api.createAdminUser.mock.calls[0][0]).toMatchObject({
            managerId: MANAGER_PROFILE.id,
        })
    })

    it('hides the Manager field when creating a manager, and sends no manager', async () => {
        const dialog = await openCreateDialog()

        api.createAdminUser.mockResolvedValue({
            id: 'u-new',
            userName: 'newmanager@example.test',
            email: 'newmanager@example.test',
            displayName: 'New Manager',
            imageUrl: '',
            emailConfirmed: true,
            isActive: true,
            roles: ['Manager'],
            inviteEmailSent: true,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newmanager@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Manager' } })
        fireEvent.click(within(dialog).getByRole('radio', { name: 'Manager' }))
        await selectDepartment(dialog)

        expect(within(dialog).queryByRole('textbox', { name: 'Manager' })).not.toBeInTheDocument()
        expect(within(dialog).queryByDisplayValue('Andreas Georgiou')).not.toBeInTheDocument()
        // Department, Job title and entitlement are still theirs to set.
        expect(within(dialog).getByDisplayValue(`${DEPARTMENT.id}`)).toBeInTheDocument()

        fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

        await waitFor(() => expect(api.createAdminUser).toHaveBeenCalledTimes(1))
        expect(api.createAdminUser.mock.calls[0][0]).toMatchObject({
            roles: ['Manager'],
            departmentId: DEPARTMENT.id,
            managerId: null,
        })
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
            isActive: true,
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

// Admins sit outside the department structure, so Create User hides the whole
// Profile section for them. A profile row is still written server-side and its
// DepartmentId is a required FK, so the panel has to supply one without asking.
describe('AdminUsersPanel — Admin hides the Profile section', () => {
    it('drops the profile fields when Admin is picked, and brings them back otherwise', async () => {
        const dialog = await openCreateDialog()

        expect(within(dialog).getByText('Profile')).toBeInTheDocument()

        fireEvent.click(within(dialog).getByRole('radio', { name: 'Admin' }))

        expect(within(dialog).queryByText('Profile')).not.toBeInTheDocument()
        expect(within(dialog).queryByLabelText(/department/i)).not.toBeInTheDocument()
        expect(within(dialog).queryByLabelText(/job title/i)).not.toBeInTheDocument()
        expect(within(dialog).queryByLabelText(/annual leave entitlement/i)).not.toBeInTheDocument()
        // The read-only Manager field goes with them — not asserted by label, since
        // "Manager" also names one of the role radios above.
        expect(within(dialog).queryByText(/set by the department's manager/i)).not.toBeInTheDocument()

        // Not a one-way door: switching back off Admin restores the section.
        fireEvent.click(within(dialog).getByRole('radio', { name: 'Manager' }))
        expect(within(dialog).getByText('Profile')).toBeInTheDocument()
    })

    /// The dialog hides the department for an Admin and used to send "the first
    /// active department" in its place, because the column was a required foreign
    /// key. That invented assignment was not invisible: it put the admin in that
    /// department's team strip and headcount on the Departments panel, in its "not
    /// checked in" warning, and in the blocker list that refused to delete it. The
    /// field it never asked about now goes unanswered.
    it('creates an admin with no department at all, not a fallback one', async () => {
        const dialog = await openCreateDialog()

        api.createAdminUser.mockResolvedValue({
            id: 'u1',
            userName: 'newadmin@example.test',
            email: 'newadmin@example.test',
            displayName: 'New Admin',
            imageUrl: '',
            emailConfirmed: true,
            isActive: true,
            roles: ['Admin'],
            inviteEmailSent: true,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newadmin@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Admin' } })
        fireEvent.click(within(dialog).getByRole('radio', { name: 'Admin' }))

        // No department to select — Create is enabled all the same.
        const create = within(dialog).getByRole('button', { name: /^create$/i })
        expect(create).toBeEnabled()
        fireEvent.click(create)

        await waitFor(() => expect(createAdminUser).toHaveBeenCalledTimes(1))

        expect(api.createAdminUser.mock.calls[0][0]).toMatchObject({
            roles: ['Admin'],
            departmentId: null,
            managerId: null,
            jobTitle: null,
        })
    })

    // An Employee or Manager still has to be placed in one: it is where their
    // manager, their leave routing and their project visibility come from.
    it('still requires a department for every other role', async () => {
        const dialog = await openCreateDialog()

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })

        for (const role of ['Employee', 'Manager']) {
            fireEvent.click(within(dialog).getByRole('radio', { name: role }))
            expect(within(dialog).getByText('Department is required')).toBeInTheDocument()
            expect(within(dialog).getByRole('button', { name: /^create$/i })).toBeDisabled()
        }
    })
})

// An Admin sits outside the department structure, so the edit dialog has to be
// able to move a profile out of a department as well as into one — and must not
// send back the department a promoted user is leaving behind.
describe('AdminUsersPanel — editing across the Admin boundary', () => {
    const EMPLOYEE_USER = { id: 'u-employee', userName: 'employee@example.test', email: 'employee@example.test', displayName: 'Theodoros Iona', imageUrl: '', emailConfirmed: true, isActive: true, roles: ['Employee'] }
    const ADMIN_USER = { id: 'u-admin', userName: 'admin@example.test', email: 'admin@example.test', displayName: 'Admin User', imageUrl: '', emailConfirmed: true, isActive: true, roles: ['Admin'] }

    const EMPLOYEE_PROFILE = { id: 'p-employee', userId: 'u-employee', displayName: 'Theodoros Iona', departmentId: DEPARTMENT.id, managerId: null, annualLeaveEntitlement: 20, leaveBalance: 20, jobTitle: null, createdAt: '2026-01-01' }
    // What the server now returns for an admin: a profile, and no department.
    const ADMIN_PROFILE = { id: 'p-admin', userId: 'u-admin', displayName: 'Admin User', departmentId: null, managerId: null, annualLeaveEntitlement: 20, leaveBalance: 20, jobTitle: null, createdAt: '2026-01-01' }

    beforeEach(() => {
        api.getAdminUsers.mockResolvedValue([EMPLOYEE_USER, ADMIN_USER] as never)
        api.getEmployeeProfiles.mockResolvedValue([EMPLOYEE_PROFILE, ADMIN_PROFILE] as never)
    })

    async function openEditFor(displayName: string) {
        renderPanel()
        const nameEl = await screen.findByText(displayName)
        const row = nameEl.parentElement!.parentElement!.parentElement!.parentElement!
        fireEvent.click(within(row).getByTitle('Edit'))
        return screen.getByRole('dialog')
    }

    it('clears the department when an employee is promoted to Admin', async () => {
        const dialog = await openEditFor('Theodoros Iona')

        await waitFor(() => expect(within(dialog).getByRole('radio', { name: 'Employee' })).toBeChecked())
        fireEvent.click(within(dialog).getByRole('radio', { name: 'Admin' }))

        fireEvent.click(within(dialog).getByRole('button', { name: /^save$/i }))

        await waitFor(() => expect(api.updateEmployeeProfile).toHaveBeenCalledTimes(1))
        // Not DEPARTMENT.id: the department the dialog stopped showing must not be
        // the one it quietly sends back.
        expect(api.updateEmployeeProfile.mock.calls[0][0]).toMatchObject({
            id: EMPLOYEE_PROFILE.id,
            departmentId: null,
        })
    })

    it('saves an admin with no department rather than inventing one', async () => {
        const dialog = await openEditFor('Admin User')

        await waitFor(() => expect(within(dialog).getByRole('radio', { name: 'Admin' })).toBeChecked())
        fireEvent.click(within(dialog).getByRole('button', { name: /^save$/i }))

        await waitFor(() => expect(api.updateEmployeeProfile).toHaveBeenCalledTimes(1))
        expect(api.updateEmployeeProfile.mock.calls[0][0]).toMatchObject({
            id: ADMIN_PROFILE.id,
            departmentId: null,
        })
    })

    // The other direction has to supply one. An admin has no department to inherit,
    // so demoting them without picking a department would save a nobody: invisible
    // to every manager, with no leave routing.
    it('will not save a demoted admin until a department is picked', async () => {
        const dialog = await openEditFor('Admin User')

        await waitFor(() => expect(within(dialog).getByRole('radio', { name: 'Admin' })).toBeChecked())
        fireEvent.click(within(dialog).getByRole('radio', { name: 'Employee' }))

        expect(within(dialog).getByText('Department is required')).toBeInTheDocument()
        expect(within(dialog).getByRole('button', { name: /^save$/i })).toBeDisabled()

        await selectDepartment(dialog)

        expect(within(dialog).getByRole('button', { name: /^save$/i })).toBeEnabled()
        fireEvent.click(within(dialog).getByRole('button', { name: /^save$/i }))

        await waitFor(() => expect(api.updateEmployeeProfile).toHaveBeenCalledTimes(1))
        expect(api.updateEmployeeProfile.mock.calls[0][0]).toMatchObject({
            id: ADMIN_PROFILE.id,
            departmentId: DEPARTMENT.id,
        })
    })
})

// Switching an account off is the answer for someone who has left: deleting them
// rewrites history (every approval they gave is nulled out), and leaving the
// account enabled leaves working credentials behind.
describe('AdminUsersPanel — activating and deactivating', () => {
    const ADMIN = { id: 'u-admin', userName: 'admin@annualleave.com', email: 'admin@annualleave.com', displayName: 'Admin User', imageUrl: '', emailConfirmed: true, isActive: true, roles: ['Admin'] }
    const ACTIVE = { id: 'u-active', userName: 'active@example.test', email: 'active@example.test', displayName: 'Still Here', imageUrl: '', emailConfirmed: true, isActive: true, roles: ['Employee'] }
    const INACTIVE = { id: 'u-inactive', userName: 'gone@example.test', email: 'gone@example.test', displayName: 'Long Gone', imageUrl: '', emailConfirmed: true, isActive: false, roles: ['Employee'] }

    beforeEach(() => {
        api.getAdminUsers.mockResolvedValue([ADMIN, ACTIVE, INACTIVE] as never)
        api.setAdminUserActive.mockResolvedValue(INACTIVE as never)
        sweetAlertFire.mockResolvedValue({ isConfirmed: true })
    })

    /** The row container, which holds both the name and the action buttons. */
    async function rowFor(displayName: string) {
        const nameEl = await screen.findByText(displayName)
        return nameEl.parentElement!.parentElement!.parentElement!.parentElement!
    }

    it('badges a deactivated account instead of showing it as merely offline', async () => {
        renderPanel()

        const row = await rowFor('Long Gone')
        expect(within(row).getByText('Deactivated')).toBeInTheDocument()
        expect(within(row).queryByText('Offline')).not.toBeInTheDocument()

        // An active account is unaffected: presence is a separate axis.
        const active = await rowFor('Still Here')
        expect(within(active).getByText('Offline')).toBeInTheDocument()
        expect(within(active).queryByText('Deactivated')).not.toBeInTheDocument()
    })

    it('deactivates an account once the admin confirms', async () => {
        renderPanel()

        const row = await rowFor('Still Here')
        fireEvent.click(within(row).getByTitle('Deactivate'))

        await waitFor(() => expect(api.setAdminUserActive).toHaveBeenCalledTimes(1))
        expect(sweetAlertFire).toHaveBeenCalled()
        expect(api.setAdminUserActive.mock.calls[0].slice(0, 2)).toEqual([ACTIVE.id, { isActive: false }])
    })

    it('leaves the account alone when the admin cancels', async () => {
        sweetAlertFire.mockResolvedValueOnce({ isConfirmed: false } as never)
        renderPanel()

        const row = await rowFor('Still Here')
        fireEvent.click(within(row).getByTitle('Deactivate'))

        await waitFor(() => expect(sweetAlertFire).toHaveBeenCalled())
        expect(api.setAdminUserActive).not.toHaveBeenCalled()
    })

    // Reactivating restores access rather than removing it, so it does not ask.
    it('reactivates a deactivated account without a confirmation', async () => {
        renderPanel()

        const row = await rowFor('Long Gone')
        expect(within(row).queryByTitle('Deactivate')).not.toBeInTheDocument()
        fireEvent.click(within(row).getByTitle('Activate'))

        await waitFor(() => expect(api.setAdminUserActive).toHaveBeenCalledTimes(1))
        expect(api.setAdminUserActive.mock.calls[0].slice(0, 2)).toEqual([INACTIVE.id, { isActive: true }])
        expect(sweetAlertFire).not.toHaveBeenCalled()
    })

    it('counts and filters deactivated accounts on their own tab', async () => {
        renderPanel()

        const tab = await screen.findByRole('button', { name: /Deactivated/ })
        expect(within(tab).getByText('1')).toBeInTheDocument()

        fireEvent.click(tab)

        expect(screen.getByText('Long Gone')).toBeInTheDocument()
        expect(screen.queryByText('Still Here')).not.toBeInTheDocument()
    })

    // The seeded admin is the account the panel already refuses to edit or
    // delete; switching it off would be just as effective a way to lose access.
    it('offers no toggle for the protected admin', async () => {
        renderPanel()

        const row = await rowFor('Admin User')
        expect(within(row).queryByTitle('Deactivate')).not.toBeInTheDocument()
        expect(within(row).queryByTitle('Activate')).not.toBeInTheDocument()
    })

    it('deactivates every selected account from the bulk bar', async () => {
        renderPanel()

        const row = await rowFor('Still Here')
        fireEvent.click(within(row).getByRole('checkbox'))

        fireEvent.click(await screen.findByText('⏸ Deactivate'))

        await waitFor(() => expect(api.setAdminUserActive).toHaveBeenCalledTimes(1))
        expect(api.setAdminUserActive.mock.calls[0].slice(0, 2)).toEqual([ACTIVE.id, { isActive: false }])
        // Deleting is a separate, still-available action — not what this button does.
        expect(api.deleteAdminUser).not.toHaveBeenCalled()
    })
})

/** MUI's select renders its options into a portal, hence the two steps. */
async function selectDepartment(dialog: HTMLElement) {
    fireEvent.mouseDown(within(dialog).getByRole('combobox'))

    const option = await screen.findByRole('option', { name: `${DEPARTMENT.name} (${DEPARTMENT.code})` })
    fireEvent.click(option)
}
