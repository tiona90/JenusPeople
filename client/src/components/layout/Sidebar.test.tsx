import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UserInfo } from '../../lib/types'
import Sidebar from './Sidebar'

/**
 * The children block on the sidebar's own Edit profile dialog exists for one
 * reason: `HasChildren` and the rows behind it are what decide whether Maternity
 * and Paternity Leave are offered to that person. A System Administrator is never offered
 * either — the role's navigation carries no "Request Leave" and no "My Leave" —
 * so the question asked them about their own family and then fed nothing.
 *
 * These tests fail if the block comes back for a System Administrator, or if hiding it ever
 * costs an employee theirs.
 */
vi.mock('../../lib/api')
vi.mock('../../lib/mobx')

const mobx = vi.mocked(await import('../../lib/mobx'))

const ADMIN: UserInfo = {
    id: 'u-admin',
    userName: 'admin@worktrack.com',
    email: 'admin@worktrack.com',
    displayName: 'Admin User',
    imageUrl: '',
    // Null, not false — a System Administrator has no department, which is what makes the
    // department field below the children block hidden for them too.
    departmentId: null,
    roles: ['System Administrator'],
}

const EMPLOYEE: UserInfo = {
    ...ADMIN,
    id: 'u-emp',
    userName: 'maria@worktrack.com',
    email: 'maria@worktrack.com',
    displayName: 'Maria Georgiou',
    departmentId: 2,
    departmentName: 'Finance',
    roles: ['Employee'],
}

beforeEach(() => {
    vi.clearAllMocks()
})

/** Renders the sidebar as `user` and opens the Edit profile dialog. */
function openEditProfile(user: UserInfo) {
    mobx.useStore.mockReturnValue({
        authStore: { user },
        uiStore: { sidebarMode: 'light' },
    } as never)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/dashboard']}>
            <QueryClientProvider client={queryClient}>
                <Sidebar />
            </QueryClientProvider>
        </MemoryRouter>,
    )

    fireEvent.click(screen.getByText(user.displayName))
    fireEvent.click(screen.getByText('Edit profile'))
}

describe('Edit profile asks about children only where the answer is used', () => {
    it('does not ask a System Administrator about their own children', () => {
        openEditProfile(ADMIN)

        expect(screen.getByRole('dialog')).toBeTruthy()
        expect(screen.queryByText('Do you have children?')).toBeNull()
    })

    it('still asks an employee', () => {
        openEditProfile(EMPLOYEE)

        expect(screen.getByText('Do you have children?')).toBeTruthy()
    })
})

/**
 * The two administrators are disjoint. An HR Administrator runs Leave & Time —
 * Leave Management, Attendance and Timesheets company-wide — and none of the system
 * administration. A System Administrator has People, Configuration and System and
 * no Leave & Time. App.tsx gates the routes behind each section the same way.
 */
describe('Navigation offered to each administrator role', () => {
    function renderSidebarAs(user: UserInfo) {
        mobx.useStore.mockReturnValue({
            authStore: { user },
            uiStore: { sidebarMode: 'light' },
        } as never)

        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        render(
            <MemoryRouter initialEntries={['/dashboard']}>
                <QueryClientProvider client={queryClient}>
                    <Sidebar />
                </QueryClientProvider>
            </MemoryRouter>,
        )
    }

    const HR_ADMIN: UserInfo = { ...ADMIN, id: 'u-hr', email: 'hr@worktrack.com', userName: 'hr@worktrack.com', displayName: 'Helen HR', roles: ['HR Administrator'] }

    it('gives an HR Administrator Leave & Time and nothing under People, Configuration or System', () => {
        renderSidebarAs(HR_ADMIN)

        expect(screen.getByText('Dashboard')).toBeInTheDocument()
        expect(screen.getByText('Leave Management')).toBeInTheDocument()
        expect(screen.getByText('Attendance')).toBeInTheDocument()
        expect(screen.getByText('Timesheets')).toBeInTheDocument()

        for (const hidden of ['People', 'Users', 'Departments', 'Configuration', 'Projects', 'Leave Types', 'System', 'Organization', 'Notification Settings', 'Data Maintenance', 'System Log']) {
            expect(screen.queryByText(hidden)).not.toBeInTheDocument()
        }
        expect(screen.getByText('HR Administrator')).toBeInTheDocument()
    })

    it('gives a System Administrator People, Configuration and System, and no Leave & Time', () => {
        renderSidebarAs(ADMIN)

        for (const shown of ['Users', 'Departments', 'Projects', 'Leave Types', 'Organization', 'Notification Settings', 'Data Maintenance', 'System Log']) {
            expect(screen.getByText(shown)).toBeInTheDocument()
        }
        for (const hidden of ['Leave & Time', 'Leave Management', 'Attendance', 'Timesheets']) {
            expect(screen.queryByText(hidden)).not.toBeInTheDocument()
        }
        expect(screen.getByText('Administrator')).toBeInTheDocument()
    })
})

function renderSidebarAs(user: UserInfo) {
    mobx.useStore.mockReturnValue({ authStore: { user }, uiStore: { sidebarMode: 'light' } } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/dashboard']}>
            <QueryClientProvider client={queryClient}>
                <Sidebar />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}

describe('Tasks is offered to Managers and HR Administrators only', () => {
    it.each([
        ['Manager', true],
        ['HR Administrator', true],
        ['Employee', false],
        ['System Administrator', false],
    ])('%s sees Tasks: %s', (role, shown) => {
        renderSidebarAs({ ...EMPLOYEE, roles: [role] as UserInfo['roles'] })
        expect(screen.queryByText('Tasks') != null).toBe(shown)
    })

    // Tasks is neither leave nor time, and not only the manager's team: it sits in a
    // section of its own, the same one for both roles, after everything else.
    it.each(['Manager', 'HR Administrator'])('puts Tasks under Collaboration for a %s', (role) => {
        renderSidebarAs({ ...EMPLOYEE, roles: [role] as UserInfo['roles'] })
        const heading = screen.getByText('Collaboration')
        const tasks = screen.getByText('Tasks')
        expect(heading.compareDocumentPosition(tasks) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
        for (const other of ['Leave Management', 'Dashboard']) {
            expect(screen.getByText(other).compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
        }
    })
})
