import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import TaskDialog from './TaskDialog'

vi.mock('../../lib/api', () => ({
    getWorkTaskDepartments: vi.fn(),
    getWorkTaskAssignees: vi.fn(),
    getWorkTaskProjects: vi.fn(),
    createWorkTask: vi.fn(),
    updateWorkTask: vi.fn(),
    updateWorkTaskStatus: vi.fn(),
}))
const api = vi.mocked(await import('../../lib/api'))

// Each test drives several MUI Selects, which jsdom renders slowly: alone the heaviest
// takes ~4s, so under a loaded machine the 5s default is a coin toss, not a signal.
vi.setConfig({ testTimeout: 15_000 })

function renderDialog() {
    const onSaved = vi.fn()
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={queryClient}>
            <TaskDialog open task={null} onClose={vi.fn()} onSaved={onSaved} />
        </QueryClientProvider>,
    )
    return { onSaved }
}

function pickBilling(option: 'Billable' | 'Non-billable') {
    fireEvent.click(screen.getByRole('radio', { name: option }))
}

async function choose(label: string, option: string) {
    fireEvent.mouseDown(screen.getByRole('combobox', { name: new RegExp(`^${label}`) }))
    fireEvent.click(await screen.findByRole('option', { name: option }))
    const listbox = screen.queryByRole('listbox')
    if (listbox) {
        fireEvent.keyDown(listbox, { key: 'Escape' })
        await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull())
    }
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getWorkTaskDepartments.mockResolvedValue([{ id: 1, name: 'Sales' }, { id: 2, name: 'Ops' }])
    api.getWorkTaskAssignees.mockImplementation(async (departmentId: number) =>
        departmentId === 1
            ? [{ userId: 'u-sam', displayName: 'Sam Sales' }, { userId: 'u-hana', displayName: 'Hana HR' }]
            : [{ userId: 'u-olga', displayName: 'Olga Ops' }, { userId: 'u-hana', displayName: 'Hana HR' }])
    api.getWorkTaskProjects.mockImplementation(async (departmentId: number) =>
        departmentId === 1 ? [{ id: 10, name: 'CRM Rollout', code: 'CRM' }] : [{ id: 12, name: 'Ops Tooling', code: 'OPT' }])
})

describe('TaskDialog', () => {
    it('loads assignees for the chosen department and clears one that no longer fits', async () => {
        renderDialog()
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        await choose('Department', 'Ops')

        await waitFor(() => expect(api.getWorkTaskAssignees).toHaveBeenCalledWith(2))
        expect(within(screen.getByRole('combobox', { name: /^Assignees/ })).queryByText('Sam Sales')).toBeNull()
    })

    it('sends no assignees for everyone in the department, and says so', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        pickBilling('Billable')

        expect(await screen.findByText(/All 2 Managers and Employees in the department/)).toBeInTheDocument()
        expect(within(screen.getByRole('combobox', { name: /^Assignees/ })).getByText('Everyone in the department')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ assigneeIds: [] }))
    })

    it('clears a picked list back to everyone from the menu', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        await choose('Assignees', 'Everyone in the department')
        pickBilling('Billable')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(api.createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ assigneeIds: [] })))
    })

    it('holds Create when the department has nobody to assign', async () => {
        api.getWorkTaskAssignees.mockResolvedValue([])
        renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        pickBilling('Billable')

        expect(await screen.findByText('Nobody in this department can be assigned a task')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Create task' })).toBeDisabled()
    })

    it('holds Create until title, department and assignee are set', async () => {
        renderDialog()
        const create = screen.getByRole('button', { name: 'Create task' })
        expect(create).toBeDisabled()

        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: '   ' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        expect(create).toBeDisabled()

        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        expect(create).toBeEnabled()
    })

    it('posts the request and reports success', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: ' Chase notes ' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith({
            title: 'Chase notes', description: null, departmentId: 1, projectId: 10, assigneeIds: ['u-sam'], dueDate: null, targetHours: null, isBillable: true, priority: 'Normal',
        })
    })

    it('opens clean after a failed save was cancelled', async () => {
        api.createWorkTask.mockRejectedValue(new Error('The assignee must be an active Manager or HR Administrator who covers this department.'))
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        const ui = (open: boolean) => (
            <QueryClientProvider client={queryClient}>
                <TaskDialog open={open} task={null} onClose={vi.fn()} onSaved={vi.fn()} />
            </QueryClientProvider>
        )
        const { rerender } = render(ui(true))
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))
        expect(await screen.findByRole('alert')).toBeInTheDocument()

        rerender(ui(false))
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
        rerender(ui(true))

        await screen.findByRole('dialog')
        expect(screen.queryByRole('alert')).toBeNull()
    })

    it("loads the chosen department's projects and clears one from another department", async () => {
        renderDialog()
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Department', 'Ops')

        await waitFor(() => expect(api.getWorkTaskProjects).toHaveBeenCalledWith(2))
        expect(within(screen.getByRole('combobox', { name: /^Project/ })).queryByText('CRM Rollout')).toBeNull()
    })

    it('holds Create until a project is chosen', async () => {
        renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        expect(screen.getByRole('button', { name: 'Create task' })).toBeDisabled()

        await choose('Project', 'CRM Rollout')
        expect(screen.getByRole('button', { name: 'Create task' })).toBeEnabled()
    })

    it('says so when the department has no active projects', async () => {
        api.getWorkTaskProjects.mockResolvedValue([])
        renderDialog()
        await choose('Department', 'Sales')

        expect(await screen.findByText('No active projects in this department')).toBeInTheDocument()
    })

    it('assigns several people and posts all of them', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        await choose('Assignees', 'Hana HR')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ assigneeIds: ['u-sam', 'u-hana'] }))
    })

    it('keeps the people who cover the new department and drops the rest', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        await choose('Assignees', 'Hana HR')
        await choose('Department', 'Ops')
        await waitFor(() => expect(api.getWorkTaskAssignees).toHaveBeenCalledWith(2))
        await choose('Project', 'Ops Tooling')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ departmentId: 2, assigneeIds: ['u-hana'] }))
    })

    it('has no Status field when creating', () => {
        renderDialog()
        expect(screen.queryByRole('combobox', { name: /^Status/ })).toBeNull()
    })

    it('saves a status changed while editing, after the details', async () => {
        const task = {
            id: 7, title: 'Chase notes', description: null, departmentId: 1, departmentName: 'Sales',
            projectId: 10, projectName: 'CRM Rollout', projectCode: 'CRM', projectColorKey: 'p1',
            assignees: [{ userId: 'u-sam', displayName: 'Sam Sales' }], createdById: 'me', createdByName: 'Me',
            dueDate: null, targetHours: null, loggedHours: 0, isBillable: true, priority: 'Normal' as const, status: 'ToDo' as const,
            createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
            canEdit: true, canChangeStatus: true,
        }
        const order: string[] = []
        api.updateWorkTask.mockImplementation(async () => { order.push('details'); return task })
        api.updateWorkTaskStatus.mockImplementation(async () => { order.push('status'); return task })
        const onSaved = vi.fn()
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        render(
            <QueryClientProvider client={queryClient}>
                <TaskDialog open task={task} onClose={vi.fn()} onSaved={onSaved} />
            </QueryClientProvider>,
        )

        await choose('Status', 'Done')
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(7, 'Done')
        expect(order).toEqual(['details', 'status'])
    })

    it('sends the target hours typed in, and offers no target weeks beside the due date', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        fireEvent.change(screen.getByRole('spinbutton', { name: /Target hours/ }), { target: { value: '40' } })
        expect(screen.queryByRole('spinbutton', { name: /Target weeks/ })).toBeNull()
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ targetHours: 40 }))
    })

    it('holds Save on a target out of range', async () => {
        renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Sam Sales')
        pickBilling('Billable')
        fireEvent.change(screen.getByRole('spinbutton', { name: /Target hours/ }), { target: { value: '0' } })

        expect(screen.getByRole('button', { name: 'Create task' })).toBeDisabled()
    })

    it('holds Create until billable or not is answered', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        await choose('Department', 'Sales')
        await choose('Project', 'CRM Rollout')
        await choose('Assignees', 'Hana HR')

        expect(screen.getByRole('radio', { name: 'Billable' })).not.toBeChecked()
        expect(screen.getByRole('radio', { name: 'Non-billable' })).not.toBeChecked()
        expect(screen.getByRole('button', { name: 'Create task' })).toBeDisabled()

        pickBilling('Non-billable')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ isBillable: false }))
    })

    it('takes an HR Administrator off a task being edited, since HR is never assigned', async () => {
        const task = {
            id: 8, title: 'Old task', description: null, departmentId: 1, departmentName: 'Sales',
            projectId: 10, projectName: 'CRM Rollout', projectCode: 'CRM', projectColorKey: 'p1',
            assignees: [
                { userId: 'u-sam', displayName: 'Sam Sales', isHrAdministrator: false },
                { userId: 'u-hr', displayName: 'Hana HR', isHrAdministrator: true },
            ],
            createdById: 'me', createdByName: 'Me', dueDate: null, targetHours: null, loggedHours: 0, isBillable: true,
            priority: 'Normal' as const, status: 'ToDo' as const,
            createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
            canEdit: true, canChangeStatus: true,
        }
        api.updateWorkTask.mockResolvedValue(task)
        const onSaved = vi.fn()
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        render(
            <QueryClientProvider client={queryClient}>
                <TaskDialog open task={task} onClose={vi.fn()} onSaved={onSaved} />
            </QueryClientProvider>,
        )

        const assignees = screen.getByRole('combobox', { name: /^Assignees/ })
        await waitFor(() => expect(within(assignees).getByText('Sam Sales')).toBeInTheDocument())
        expect(within(assignees).queryByText('Hana HR')).toBeNull()

        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.updateWorkTask).toHaveBeenCalledWith(8, expect.objectContaining({ assigneeIds: ['u-sam'] }))
    })
})
