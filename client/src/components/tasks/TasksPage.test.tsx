import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { WorkTask } from '../../lib/types'
import TasksPage from './TasksPage'

vi.mock('../../lib/api', () => ({
    getWorkTasks: vi.fn(),
    getWorkTaskDepartments: vi.fn(),
    getWorkTaskAssignees: vi.fn(),
    getWorkTaskProjects: vi.fn(),
    createWorkTask: vi.fn(),
    updateWorkTask: vi.fn(),
    updateWorkTaskStatus: vi.fn(),
    deleteWorkTask: vi.fn(),
}))
vi.mock('../../lib/mobx')
vi.mock('../ui/SweetAlert', () => ({ default: { fire: vi.fn() } }))

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))
const sweetAlert = vi.mocked((await import('../ui/SweetAlert')).default)

const base: WorkTask = {
    id: 1, title: 'Mine to do', description: null, departmentId: 1, departmentName: 'Sales',
    projectId: 10, projectName: 'CRM Rollout', projectCode: 'CRM', projectColorKey: 'p1',
    assignees: [{ userId: 'me', displayName: 'Me' }], createdById: 'boss', createdByName: 'Boss',
    dueDate: '2020-01-01', targetHours: 40, loggedHours: 0, isBillable: true, priority: 'High', status: 'ToDo',
    createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
    canEdit: false, canChangeStatus: true,
}
const TASKS: WorkTask[] = [
    base,
    { ...base, id: 2, title: 'I asked for this', assignees: [{ userId: 'x', displayName: 'Xena' }, { userId: 'z', displayName: 'Zoe' }], createdById: 'me', createdByName: 'Me', dueDate: null, targetHours: null, isBillable: false, canEdit: true },
    { ...base, id: 3, title: "Someone else's", assignees: [{ userId: 'x', displayName: 'Xena' }], createdById: 'y', createdByName: 'Yan', dueDate: null, canEdit: false, canChangeStatus: false },
]

function renderPage(roles: string[] = ['Manager']) {
    mobx.useStore.mockReturnValue({ authStore: { user: { id: 'me', roles } } } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/tasks']}>
            <QueryClientProvider client={queryClient}>
                <TasksPage />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}

const cards = () => screen.findAllByTestId('task-card')
const cardFor = async (title: string) => (await cards()).find((c) => within(c).queryByText(title))!
const showView = (view: 'assigned' | 'created' | 'all') =>
    fireEvent.change(screen.getByRole('combobox', { name: 'View' }), { target: { value: view } })

beforeEach(() => {
    vi.clearAllMocks()
    api.getWorkTasks.mockResolvedValue(TASKS)
    api.getWorkTaskDepartments.mockResolvedValue([{ id: 1, name: 'Sales' }])
})

describe('TasksPage', () => {
    it('opens on Assigned to me, with open counts in the view picker', async () => {
        renderPage()
        const shown = await cards()
        expect(shown).toHaveLength(1)
        expect(within(shown[0]).getByText('Mine to do')).toBeInTheDocument()

        const view = screen.getByRole('combobox', { name: 'View' })
        expect(within(view).getByRole('option', { name: 'Assigned to me (1)' })).toBeInTheDocument()
        expect(within(view).getByRole('option', { name: 'Created by me (1)' })).toBeInTheDocument()
        expect(within(view).getByRole('option', { name: 'All in my departments (3)' })).toBeInTheDocument()
    })

    it('sums the tasks up in the tiles', async () => {
        renderPage()
        await cards()
        const open = screen.getByTestId('stat-open')
        expect(within(open).getByText('3')).toBeInTheDocument()
        expect(within(screen.getByTestId('stat-overdue')).getByText('1')).toBeInTheDocument()
        expect(within(screen.getByTestId('stat-mine')).getByText('1')).toBeInTheDocument()
    })

    it('marks an open task past its due date as overdue', async () => {
        renderPage()
        const card = await cardFor('Mine to do')
        expect(within(card).getByText(/Overdue by \d+ days/)).toBeInTheDocument()
    })

    it('shows the project badge and name on the card', async () => {
        renderPage()
        const card = await cardFor('Mine to do')
        expect(within(card).getByText('CRM')).toBeInTheDocument()
        expect(within(card).getByText('CRM Rollout')).toBeInTheDocument()
    })

    it('shows every assignee as an avatar with their name on hover', async () => {
        renderPage()
        await cards()
        showView('created')

        const card = await cardFor('I asked for this')
        expect(within(card).getByTitle('Xena')).toBeInTheDocument()
        expect(within(card).getByTitle('Zoe')).toBeInTheDocument()
        expect(within(card).getByText('2')).toBeInTheDocument()
    })

    it('offers Edit and Delete only on tasks the viewer may manage, and locks status for bystanders', async () => {
        renderPage()
        await cards()
        showView('all')

        const mine = await cardFor('I asked for this')
        expect(within(mine).getByRole('button', { name: /Edit/ })).toBeInTheDocument()
        expect(within(await cardFor('Mine to do')).queryByRole('button', { name: /Edit/ })).toBeNull()
        const bystander = await cardFor("Someone else's")
        expect(within(bystander).queryByRole('button', { name: /Start/ })).toBeNull()
        expect(within(bystander).queryByRole('button', { name: /Status/ })).toBeNull()
    })

    it('narrows the grid with the search box', async () => {
        renderPage()
        await cards()
        showView('all')
        fireEvent.change(screen.getByPlaceholderText('Search tasks…'), { target: { value: 'someone' } })

        await waitFor(async () => expect(await cards()).toHaveLength(1))
        expect(screen.getByText("Someone else's")).toBeInTheDocument()
    })

    it('asks before deleting, then deletes', async () => {
        sweetAlert.fire.mockResolvedValue({ isConfirmed: true } as never)
        api.deleteWorkTask.mockResolvedValue(undefined)
        renderPage()
        await cards()
        showView('created')

        fireEvent.click(within(await cardFor('I asked for this')).getByRole('button', { name: /Delete/ }))

        await waitFor(() => expect(api.deleteWorkTask).toHaveBeenCalledWith(2))
        expect(sweetAlert.fire).toHaveBeenCalledWith(expect.objectContaining({ title: 'Delete "I asked for this"?' }))
    })

    it('says so when a view is empty', async () => {
        api.getWorkTasks.mockResolvedValue([])
        renderPage()
        expect(await screen.findByText('Nothing assigned to you.')).toBeInTheDocument()
    })

    it('moves a task on with its next-step button', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        renderPage()
        const card = await cardFor('Mine to do')

        fireEvent.click(within(card).getByRole('button', { name: /Start/ }))

        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(1, 'InProgress'))
    })

    it('offers every status from the Status menu, marking the current one', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        renderPage()
        const card = await cardFor('Mine to do')

        fireEvent.click(within(card).getByRole('button', { name: /Status/ }))
        const menu = await screen.findByRole('menu')
        expect(within(menu).getAllByRole('menuitem').map((i) => i.textContent?.replace('✓', ''))).toEqual(['To do', 'In progress', 'Done', 'Cancelled'])
        expect(within(menu).getByRole('menuitem', { name: 'To do' })).toHaveAttribute('aria-current', 'true')

        fireEvent.click(within(menu).getByRole('menuitem', { name: 'Cancelled' }))
        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(1, 'Cancelled'))
    })

    it('quotes the target on the card, and says when there is none', async () => {
        renderPage()
        const planned = await cardFor('Mine to do')
        expect(within(planned).getByText('40h')).toBeInTheDocument()
        expect(within(planned).getByText('0h logged · 40h left')).toBeInTheDocument()

        showView('created')
        const unplanned = await cardFor('I asked for this')
        expect(within(unplanned).getByText('no target set')).toBeInTheDocument()
    })

    it('badges each card billable or non-billable', async () => {
        renderPage()
        expect(within(await cardFor('Mine to do')).getByText('Billable')).toBeInTheDocument()
        showView('created')
        expect(within(await cardFor('I asked for this')).getByText('Non-billable')).toBeInTheDocument()
    })

    it('gives an Employee their tasks to work, and nothing to create or pick between', async () => {
        api.getWorkTasks.mockResolvedValue([base])
        renderPage(['Employee'])

        const card = await cardFor('Mine to do')
        expect(within(card).getByRole('button', { name: /Start/ })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /New task/ })).toBeNull()
        expect(screen.queryByText('Create a new task')).toBeNull()
        expect(screen.queryByRole('combobox', { name: 'View' })).toBeNull()
        expect(api.getWorkTaskDepartments).not.toHaveBeenCalled()
    })

    it('gives an HR Administrator no Assigned to me, opens on their departments, and filters by department', async () => {
        renderPage(['HR Administrator'])
        expect(await cards()).toHaveLength(3)

        const view = screen.getByRole('combobox', { name: 'View' })
        expect(within(view).queryByRole('option', { name: /Assigned to me/ })).toBeNull()
        expect(within(view).getByRole('option', { name: 'Created by me (1)' })).toBeInTheDocument()
        expect(view).toHaveValue('all')
        expect(screen.queryByTestId('stat-mine')).toBeNull()

        const department = await screen.findByRole('combobox', { name: 'Department filter' })
        expect(within(department).getByRole('option', { name: 'Sales' })).toBeInTheDocument()
    })
})

describe('target progress', () => {
    it('shows the hours logged and left against the target, and how far over', async () => {
        api.getWorkTasks.mockResolvedValue([
            { ...base, id: 11, title: 'on track', targetHours: 24, loggedHours: 8 },
            { ...base, id: 12, title: 'overrun', targetHours: 10, loggedHours: 14 },
        ])
        renderPage()
        expect(within(await cardFor('on track')).getByText('8h logged · 16h left')).toBeInTheDocument()
        expect(within(await cardFor('overrun')).getByText('4h over')).toBeInTheDocument()
    })
})
