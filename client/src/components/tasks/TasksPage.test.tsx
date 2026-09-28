import { fireEvent, render, screen, within } from '@testing-library/react'
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

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const base: WorkTask = {
    id: 1, title: 'Mine to do', description: null, departmentId: 1, departmentName: 'Sales', projectId: 10, projectName: 'CRM Rollout',
    assigneeId: 'me', assigneeName: 'Me', createdById: 'boss', createdByName: 'Boss',
    dueDate: '2020-01-01', priority: 'High', status: 'ToDo',
    createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
    canEdit: false, canChangeStatus: true,
}
const TASKS: WorkTask[] = [
    base,
    { ...base, id: 2, title: 'I asked for this', assigneeId: 'x', assigneeName: 'Xena', createdById: 'me', createdByName: 'Me', dueDate: null, canEdit: true },
    { ...base, id: 3, title: 'Someone else\'s', assigneeId: 'x', assigneeName: 'Xena', createdById: 'y', createdByName: 'Yan', dueDate: null, canEdit: false, canChangeStatus: false },
]

function renderPage() {
    mobx.useStore.mockReturnValue({ authStore: { user: { id: 'me' } } } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={['/tasks']}>
            <QueryClientProvider client={queryClient}>
                <TasksPage />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getWorkTasks.mockResolvedValue(TASKS)
    api.getWorkTaskDepartments.mockResolvedValue([{ id: 1, name: 'Sales' }])
})

describe('TasksPage', () => {
    it('opens on Assigned to me, with open counts on each tab', async () => {
        renderPage()
        const rows = await screen.findAllByTestId('task-row')
        expect(rows).toHaveLength(1)
        expect(within(rows[0]).getByText('Mine to do')).toBeInTheDocument()
        expect(screen.getByRole('tab', { name: /Assigned to me\s*1/ })).toBeInTheDocument()
        expect(screen.getByRole('tab', { name: /Created by me\s*1/ })).toBeInTheDocument()
        expect(screen.getByRole('tab', { name: /All in my departments\s*3/ })).toBeInTheDocument()
    })

    it('marks an open task past its due date as overdue', async () => {
        renderPage()
        const row = (await screen.findAllByTestId('task-row'))[0]
        expect(within(row).getByText(/Overdue/)).toBeInTheDocument()
    })

    it('shows Edit only on tasks the viewer created, and locks status for bystanders', async () => {
        renderPage()
        await screen.findAllByTestId('task-row')
        fireEvent.click(screen.getByRole('tab', { name: /All in my departments/ }))

        const rows = await screen.findAllByTestId('task-row')
        const byTitle = (title: string) => rows.find((r) => within(r).queryByText(title))!
        expect(within(byTitle('I asked for this')).getByRole('button', { name: 'Task actions' })).toBeInTheDocument()
        expect(within(byTitle('Mine to do')).queryByRole('button', { name: 'Task actions' })).toBeNull()
        expect(within(byTitle("Someone else's")).getByRole('combobox', { name: 'Status' })).toHaveAttribute('aria-disabled', 'true')
    })

    it('says so when a tab is empty', async () => {
        api.getWorkTasks.mockResolvedValue([])
        renderPage()
        expect(await screen.findByText('Nothing assigned to you.')).toBeInTheDocument()
    })

    it("shows the task's project on its row", async () => {
        renderPage()
        const row = (await screen.findAllByTestId('task-row'))[0]
        expect(within(row).getByText('CRM Rollout')).toBeInTheDocument()
    })
})
