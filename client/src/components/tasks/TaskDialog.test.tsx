import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import TaskDialog from './TaskDialog'

vi.mock('../../lib/api', () => ({
    getWorkTaskDepartments: vi.fn(),
    getWorkTaskAssignees: vi.fn(),
    createWorkTask: vi.fn(),
    updateWorkTask: vi.fn(),
}))
const api = vi.mocked(await import('../../lib/api'))

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

async function choose(label: string, option: string) {
    fireEvent.mouseDown(screen.getByRole('combobox', { name: new RegExp(`^${label}`) }))
    fireEvent.click(await screen.findByRole('option', { name: option }))
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getWorkTaskDepartments.mockResolvedValue([{ id: 1, name: 'Sales' }, { id: 2, name: 'Ops' }])
    api.getWorkTaskAssignees.mockImplementation(async (departmentId: number) =>
        departmentId === 1 ? [{ userId: 'u-sam', displayName: 'Sam Sales' }] : [{ userId: 'u-olga', displayName: 'Olga Ops' }])
})

describe('TaskDialog', () => {
    it('loads assignees for the chosen department and clears one that no longer fits', async () => {
        renderDialog()
        await choose('Department', 'Sales')
        await choose('Assignee', 'Sam Sales')
        await choose('Department', 'Ops')

        await waitFor(() => expect(api.getWorkTaskAssignees).toHaveBeenCalledWith(2))
        expect(within(screen.getByRole('combobox', { name: /^Assignee/ })).queryByText('Sam Sales')).toBeNull()
    })

    it('holds Create until title, department and assignee are set', async () => {
        renderDialog()
        const create = screen.getByRole('button', { name: 'Create task' })
        expect(create).toBeDisabled()

        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: '   ' } })
        await choose('Department', 'Sales')
        await choose('Assignee', 'Sam Sales')
        expect(create).toBeDisabled()

        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: 'Chase notes' } })
        expect(create).toBeEnabled()
    })

    it('posts the request and reports success', async () => {
        api.createWorkTask.mockResolvedValue({} as never)
        const { onSaved } = renderDialog()
        fireEvent.change(screen.getByRole('textbox', { name: /^Title/ }), { target: { value: ' Chase notes ' } })
        await choose('Department', 'Sales')
        await choose('Assignee', 'Sam Sales')
        fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(onSaved).toHaveBeenCalled())
        expect(api.createWorkTask).toHaveBeenCalledWith({
            title: 'Chase notes', description: null, departmentId: 1, assigneeId: 'u-sam', dueDate: null, priority: 'Normal',
        })
    })
})
