import { fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { StoreProvider } from '../../lib/mobx'
import type { AdminUser, CompanyAttendance, Department, EmployeeProfile } from '../../lib/types'
import DepartmentsPanel from './DepartmentsPanel'

// Every department used to read "Needs attention" — an empty shell with no manager was
// flagged like a fault, said so twice in stacked banners, and left the stat card
// breakdown ("0 healthy · 0 inactive") and the filter ("Active (0)") accounting for
// nothing. "Active" now means the isActive flag, the same as the dashboard's "N active".
vi.mock('../../lib/api', () => ({
    getAppSettings: vi.fn(),
    getDepartments: vi.fn(),
    getLeaveTypes: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getAdminUsers: vi.fn(),
    getCompanyAttendance: vi.fn(),
    getAnnualLeaves: vi.fn(),
    getTimesheets: vi.fn(),
    createDepartment: vi.fn(),
    updateDepartment: vi.fn(),
    deleteDepartment: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

function dept(id: number, name: string, code: string, isActive = true): Department {
    return { id, name, code, isActive, createdAt: '2026-01-01T00:00:00' }
}

function profile(userId: string, departmentId: number): EmployeeProfile {
    return {
        id: `pr-${userId}`, userId, displayName: userId, departmentId, managerId: null,
        annualLeaveEntitlement: 20, leaveBalance: 20, jobTitle: null, createdAt: '2026-01-01T00:00:00',
    }
}

function user(id: string, roles: AdminUser['roles']): AdminUser {
    return {
        id, userName: id, email: `${id}@annualleave.com`, displayName: id,
        imageUrl: '', emailConfirmed: true, isActive: true, roles,
    }
}

const ENGINEERING = dept(1, 'Engineering', 'ENG')
const FINANCE = dept(3, 'Finance', 'FIN')
const HR = dept(2, 'Human Resources', 'HR')
const MARKETING = dept(4, 'Marketing', 'MKT')
const OPERATIONS = dept(5, 'Operations', 'OPS')

/* Engineering and Finance are staffed and managed but mostly not checked in. HR,
   Marketing and Operations have nobody in them and no manager. */
const PROFILES = [
    profile('eng-manager', ENGINEERING.id), profile('eng-1', ENGINEERING.id), profile('eng-2', ENGINEERING.id),
    profile('fin-manager', FINANCE.id), profile('fin-1', FINANCE.id), profile('fin-2', FINANCE.id),
]

const USERS = [
    user('eng-manager', ['Manager']), user('eng-1', ['Employee']), user('eng-2', ['Employee']),
    user('fin-manager', ['Manager']), user('fin-1', ['Employee']), user('fin-2', ['Employee']),
]

const ATTENDANCE = {
    total: 6, in: 0, break: 0, out: 6, leave: 0, totalMinutesToday: 0, avgMinutesToday: 0,
    departments: [
        { name: 'Engineering', total: 3, in: 0, break: 0, out: 3, leave: 0, totalMinutes: 0, avgMinutes: 0 },
        { name: 'Finance', total: 3, in: 0, break: 0, out: 3, leave: 0, totalMinutes: 0, avgMinutes: 0 },
    ],
    recent: [], issues: [],
} as CompanyAttendance

beforeEach(() => {
    vi.clearAllMocks()
    api.getDepartments.mockResolvedValue([ENGINEERING, FINANCE, HR, MARKETING, OPERATIONS])
    api.getEmployeeProfiles.mockResolvedValue(PROFILES)
    api.getAdminUsers.mockResolvedValue(USERS)
    api.getCompanyAttendance.mockResolvedValue(ATTENDANCE)
    api.getAnnualLeaves.mockResolvedValue([])
    api.getTimesheets.mockResolvedValue([])
    api.getLeaveTypes.mockResolvedValue([])
    api.getAppSettings.mockResolvedValue({ defaultAnnualEntitlement: 20 } as never)
})

async function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <DepartmentsPanel />
            </QueryClientProvider>
        </StoreProvider>,
    )
    await screen.findByPlaceholderText('Search departments…')
    return view
}

/** The card for one department, found from its name heading. */
function card(name: string) {
    // name → text column → header row → header → card root
    let el = screen.getByText(name)
    for (let i = 0; i < 4; i++) el = el.parentElement!
    return el
}

/** The value and caption of a stat card. */
function statCard(label: string) {
    const root = screen.getByText(label).parentElement!
    return { value: root.children[1].textContent, sub: root.children[2].textContent }
}

/**
 * The "N people" count beside a card's "Team members" heading. Read off the
 * heading's sibling rather than by text, because a bare "3" also matches the
 * pending/leave stat tiles further down the same card.
 */
function headcountOf(name: string) {
    const label = within(card(name)).getByText('Team members')
    return label.parentElement!.children[1].textContent
}

function statusOptions() {
    const select = screen.getAllByRole('combobox')
        .find((s) => Array.from(s.querySelectorAll('option')).some((o) => (o as HTMLOptionElement).value === 'attention'))!
    return { select, labels: Array.from(select.querySelectorAll('option')).map((o) => o.textContent) }
}

/** Switches to the table view and reads each row's name and status pill. */
function tableStatuses() {
    fireEvent.click(screen.getByText('☰ Table'))
    return screen.getAllByRole('row').slice(1).map((row) => {
        const cells = row.querySelectorAll('td')
        const status = Array.from(cells).map((c) => c.textContent!.trim())
            .find((t) => ['Active', 'Attention', 'Not set up', 'Inactive'].includes(t))
        return `${cells[0].textContent!.trim()}=${status}`
    })
}

describe('DepartmentsPanel — department status', () => {
    it('calls an empty unmanaged department not set up yet, not a problem', async () => {
        await renderPanel()

        expect(within(card('Engineering')).getByText('Needs attention')).toBeInTheDocument()
        expect(within(card('Finance')).getByText('Needs attention')).toBeInTheDocument()
        for (const name of ['Human Resources', 'Marketing', 'Operations']) {
            expect(within(card(name)).getByText('Not set up yet')).toBeInTheDocument()
            expect(within(card(name)).queryByText('Needs attention')).not.toBeInTheDocument()
        }
    })

    it('still flags a staffed department whose manager post is vacant', async () => {
        // Same people, but nobody in Engineering holds the Manager role.
        api.getAdminUsers.mockResolvedValue(USERS.map((u) =>
            u.id === 'eng-manager' ? user(u.id, ['Employee']) : u))
        await renderPanel()

        expect(within(card('Engineering')).getByText('Needs attention')).toBeInTheDocument()
        expect(within(card('Engineering')).getByText('No manager assigned')).toBeInTheDocument()
    })

    it('says the manager post is vacant once, in the banner that says what it costs', async () => {
        await renderPanel()

        const hr = card('Human Resources')
        expect(within(hr).getByText('No manager assigned')).toBeInTheDocument()
        expect(within(hr).getByText('Approvals are routed to Admin')).toBeInTheDocument()
        expect(within(hr).queryByText(/Manager position vacant/)).not.toBeInTheDocument()
    })

    it('keeps the real alert on a department that has one', async () => {
        await renderPanel()

        expect(within(card('Engineering')).getByText('3 employees not checked in')).toBeInTheDocument()
        expect(within(card('Human Resources')).queryByText(/not checked in/)).not.toBeInTheDocument()
    })

    it('breaks the department count into the buckets the filter offers', async () => {
        await renderPanel()

        // Accounts for all five, and "active" means what the dashboard means by it.
        expect(statCard('🏢 Departments')).toEqual({ value: '5', sub: '5 active · 0 inactive' })
        expect(statCard('⚠️ Need Attention')).toEqual({ value: '2', sub: 'departments flagged · 3 not set up yet' })
        expect(statusOptions().labels).toEqual([
            'All statuses (5)',
            'Active (5)',
            'Needs attention (2)',
            'Not set up yet (3)',
            'Inactive (0)',
        ])
    })

    it('returns the rows each filter option promises', async () => {
        await renderPanel()
        const { select } = statusOptions()

        fireEvent.change(select, { target: { value: 'active' } })
        expect(tableStatuses()).toHaveLength(5)

        fireEvent.change(select, { target: { value: 'attention' } })
        expect(tableStatuses()).toEqual(['Engineering=Attention', 'Finance=Attention'])

        fireEvent.change(select, { target: { value: 'unconfigured' } })
        expect(tableStatuses()).toEqual([
            'Human Resources=Not set up', 'Marketing=Not set up', 'Operations=Not set up',
        ])
    })

    it('counts an archived department as inactive, matching the dashboard', async () => {
        api.getDepartments.mockResolvedValue([
            ENGINEERING, FINANCE, HR, MARKETING, dept(OPERATIONS.id, 'Operations', 'OPS', false),
        ])
        await renderPanel()

        expect(statCard('🏢 Departments')).toEqual({ value: '5', sub: '4 active · 1 inactive' })
        expect(statusOptions().labels).toContain('Active (4)')
        expect(statusOptions().labels).toContain('Inactive (1)')
        // An archived department is inactive, not "not set up yet".
        expect(statusOptions().labels).toContain('Not set up yet (2)')
        expect(within(card('Operations')).getByText('Inactive')).toBeInTheDocument()
    })
})

// The screenshot that started this: the seeded admin sat in Engineering's team
// strip, counted toward its headcount and toward its "employees not checked in"
// warning. Nobody had put them there — EmployeeProfile.DepartmentId was a required
// foreign key, so the seeder and the create dialog each invented a department for
// an account the panel deliberately keeps outside the structure.
describe('DepartmentsPanel — an admin belongs to no department', () => {
    const ADMIN_PROFILE: EmployeeProfile = {
        id: 'pr-admin', userId: 'admin', displayName: 'Admin User', departmentId: null,
        managerId: null, annualLeaveEntitlement: 20, leaveBalance: 20, jobTitle: null,
        createdAt: '2026-01-01T00:00:00',
    }

    beforeEach(() => {
        api.getEmployeeProfiles.mockResolvedValue([...PROFILES, ADMIN_PROFILE])
        api.getAdminUsers.mockResolvedValue([...USERS, user('admin', ['Admin'])])
    })

    it('leaves the admin out of every department card', async () => {
        await renderPanel()

        for (const name of ['Engineering', 'Finance', 'Human Resources', 'Marketing', 'Operations']) {
            expect(within(card(name)).queryByTitle('Admin User')).not.toBeInTheDocument()
        }
    })

    it('leaves the admin out of every headcount', async () => {
        await renderPanel()

        // Three each, as before the admin existed — not four.
        expect(headcountOf('Engineering')).toBe('3people')
        expect(headcountOf('Finance')).toBe('3people')
        for (const name of ['Human Resources', 'Marketing', 'Operations']) {
            expect(within(card(name)).getByText('No members yet')).toBeInTheDocument()
        }
    })

    // The stat card counts people the departments actually hold, so a
    // department-less admin is not one of them.
    it('leaves the admin out of the company headcount', async () => {
        await renderPanel()

        expect(statCard('👥 Total Headcount').value).toBe('6')
    })
})
