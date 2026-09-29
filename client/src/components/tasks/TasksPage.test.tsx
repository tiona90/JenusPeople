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
    getIdleTaskPeople: vi.fn(),
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
    api.getIdleTaskPeople.mockResolvedValue([])
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

    it('keeps the card to a summary and opens the whole task, files to download, on a click', async () => {
        const long = 'Line one of the brief.\nLine two.\nLine three, which the card never shows in full.'
        api.getWorkTasks.mockResolvedValue([{
            ...base, description: long,
            attachments: [{
                id: 5, fileName: 'brief.pdf', contentType: 'application/pdf', sizeBytes: 2048, url: '/api/files/f1',
                uploadedByName: 'Boss', createdAtUtc: '2026-09-01T08:00:00', canRemove: false,
            }],
        }])
        renderPage()
        const card = await cardFor('Mine to do')

        // The card names the files but lists none of them.
        expect(within(card).getByText(/1 attachment/)).toBeInTheDocument()
        expect(within(card).queryByRole('link', { name: 'brief.pdf' })).toBeNull()

        fireEvent.click(card)
        const dialog = await screen.findByRole('dialog')
        expect(within(dialog).getByText((_, el) => el?.textContent === long && el.children.length === 0)).toBeInTheDocument()
        expect(within(dialog).getByRole('link', { name: 'Download brief.pdf' })).toHaveAttribute('download', 'brief.pdf')
        expect(within(dialog).getByRole('link', { name: 'brief.pdf' })).toHaveAttribute('href', '/api/files/f1')
        // Somebody else's task: nothing to attach, and no Edit.
        expect(within(dialog).queryByRole('button', { name: /Attach files/ })).toBeNull()
        expect(within(dialog).queryByRole('button', { name: 'Edit task' })).toBeNull()
    })

    it('lists for a manager who is not working on a task, following the department filter', async () => {
        api.getWorkTaskDepartments.mockResolvedValue([{ id: 1, name: 'Sales' }, { id: 2, name: 'Ops' }])
        api.getIdleTaskPeople.mockResolvedValue([
            { userId: 'a', displayName: 'Ann Idle', isManager: false, departmentIds: [1], departmentNames: ['Sales'], toDoCount: 0 },
            { userId: 'b', displayName: 'Ben Waiting', isManager: true, departmentIds: [2], departmentNames: ['Ops'], toDoCount: 2 },
        ])
        renderPage()

        const panel = await screen.findByRole('region', { name: 'Not working on a task' })
        expect(within(panel).getByTestId('idle-count')).toHaveTextContent('2')
        expect(within(panel).getByText('No tasks')).toBeInTheDocument()
        expect(within(panel).getByText('2 to do, none started')).toBeInTheDocument()

        fireEvent.change(await screen.findByRole('combobox', { name: 'Department filter' }), { target: { value: '2' } })
        expect(within(panel).queryByText('Ann Idle')).toBeNull()
        expect(within(panel).getByText('Ben Waiting')).toBeInTheDocument()
    })

    it('shows an Employee no idle-people panel', async () => {
        renderPage(['Employee'])
        await cards()
        expect(screen.queryByRole('region', { name: 'Not working on a task' })).toBeNull()
        expect(api.getIdleTaskPeople).not.toHaveBeenCalled()
    })

    it('does not open the details for a click on the status controls', async () => {
        renderPage()
        const card = await cardFor('Mine to do')
        fireEvent.click(within(card).getByRole('button', { name: /Start/ }))
        expect(screen.queryByRole('dialog')).toBeNull()
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

    it('gives an Employee their tasks to work and their own to create, with no views to pick between', async () => {
        api.getWorkTasks.mockResolvedValue([base, { ...base, id: 4, title: 'My own', createdById: 'me', createdByName: 'Me', canEdit: true }])
        renderPage(['Employee'])

        const card = await cardFor('Mine to do')
        expect(within(card).getByRole('button', { name: /Start/ })).toBeInTheDocument()
        expect(within(card).queryByRole('button', { name: /Edit/ })).toBeNull()
        expect(within(await cardFor('My own')).getByRole('button', { name: /Edit/ })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /New task/ })).toBeInTheDocument()
        expect(screen.queryByRole('combobox', { name: 'View' })).toBeNull()
    })

    it("puts the work handed to an Employee first and their own tasks apart underneath", async () => {
        api.getWorkTasks.mockResolvedValue([base, { ...base, id: 4, title: 'My own', createdById: 'me', createdByName: 'Me', canEdit: true }])
        renderPage(['Employee'])

        const handed = await screen.findByRole('region', { name: 'From your manager & HR' })
        const own = screen.getByRole('region', { name: 'My own tasks' })
        expect(within(handed).getByText('Mine to do')).toBeInTheDocument()
        expect(within(handed).queryByText('My own')).toBeNull()
        expect(within(own).getByText('My own')).toBeInTheDocument()
        expect(within(own).getByText('Add a task of your own')).toBeInTheDocument()
        expect(handed.compareDocumentPosition(own) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
        expect(within(screen.getByTestId('stat-mine')).getByText(/From Manager & HR/)).toBeInTheDocument()
    })

    it('shows an HR Administrator everything in their departments with no view picker, and filters by department', async () => {
        renderPage(['HR Administrator'])
        expect(await cards()).toHaveLength(3)

        expect(screen.queryByRole('combobox', { name: 'View' })).toBeNull()
        expect(screen.queryByTestId('stat-mine')).toBeNull()

        const department = await screen.findByRole('combobox', { name: 'Department filter' })
        expect(within(department).getByRole('option', { name: 'Sales' })).toBeInTheDocument()
    })

    it('offers an HR Administrator a CSV of the tasks the filters show, and nobody else', async () => {
        URL.createObjectURL = vi.fn(() => 'blob:tasks')
        URL.revokeObjectURL = vi.fn()
        const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
        renderPage(['HR Administrator'])
        await cards()
        fireEvent.change(screen.getByPlaceholderText('Search tasks…'), { target: { value: 'asked' } })

        fireEvent.click(screen.getByRole('button', { name: /Export CSV/ }))

        expect(click).toHaveBeenCalled()
        const blob = vi.mocked(URL.createObjectURL).mock.calls[0][0] as Blob
        const lines = (await new Promise<string>((resolve) => { const r = new FileReader(); r.onload = () => resolve(r.result as string); r.readAsText(blob) })).replace('\uFEFF', '').split('\r\n')
        expect(lines).toHaveLength(2)
        expect(lines[1]).toMatch(/^I asked for this,/)
        click.mockRestore()
    })

    it('gives a Manager no CSV export', async () => {
        renderPage(['Manager'])
        await cards()
        expect(screen.queryByRole('button', { name: /Export CSV/ })).toBeNull()
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

describe('TasksPage confirmation', () => {
    it('gives a reviewer Confirm and Send back on a waiting task', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        api.getWorkTasks.mockResolvedValue([{ ...TASKS[1], status: 'AwaitingConfirmation', canConfirm: true }])
        renderPage()
        await screen.findByRole('combobox', { name: 'View' })
        showView('created')
        const card = await cardFor('I asked for this')

        fireEvent.click(within(card).getByRole('button', { name: /Confirm/ }))
        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(2, 'Done'))
    })

    it('asks the reviewer why before sending a task back', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        api.getWorkTasks.mockResolvedValue([{ ...TASKS[1], status: 'AwaitingConfirmation', canConfirm: true }])
        renderPage()
        await screen.findByRole('combobox', { name: 'View' })
        showView('created')
        const card = await cardFor('I asked for this')

        fireEvent.click(within(card).getByRole('button', { name: /Send back/ }))
        const dialog = await screen.findByRole('dialog')
        const send = within(dialog).getByRole('button', { name: 'Send back' })
        expect(send).toBeDisabled()
        fireEvent.change(within(dialog).getByRole('textbox', { name: /Reason/ }), { target: { value: '   ' } })
        expect(send).toBeDisabled()
        fireEvent.change(within(dialog).getByRole('textbox', { name: /Reason/ }), { target: { value: 'Totals are off' } })
        fireEvent.click(send)

        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(2, 'InProgress', 'Totals are off'))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    })

    it('tells the assignee who will confirm, and lets them withdraw', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        api.getWorkTasks.mockResolvedValue([{ ...base, status: 'AwaitingConfirmation', canConfirm: false }])
        renderPage()
        const card = await cardFor('Mine to do')

        expect(within(card).getByText('Waiting for Boss to confirm')).toBeInTheDocument()
        expect(within(card).queryByRole('button', { name: /Confirm/ })).not.toBeInTheDocument()
        fireEvent.click(within(card).getByRole('button', { name: /Withdraw/ }))
        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(1, 'InProgress'))
    })

    it('shows why a task was sent back', async () => {
        api.getWorkTasks.mockResolvedValue([{ ...base, status: 'InProgress', sentBackReason: 'Totals are off' }])
        renderPage()
        expect(within(await cardFor('Mine to do')).getByText('Sent back: Totals are off')).toBeInTheDocument()
    })

    it('counts the tasks waiting for my confirmation in a tile', async () => {
        api.getWorkTasks.mockResolvedValue([{ ...TASKS[1], status: 'AwaitingConfirmation', canConfirm: true }])
        renderPage()
        expect(within(await screen.findByTestId('stat-confirm')).getByText('1')).toBeInTheDocument()
    })
})
