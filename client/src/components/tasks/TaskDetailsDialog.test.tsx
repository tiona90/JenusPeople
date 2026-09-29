import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import TaskDetailsDialog from './TaskDetailsDialog'
import { DEFAULT_TASK_SETTINGS } from '../../lib/task-settings'
import type { WorkTask } from '../../lib/types'

vi.mock('../../lib/api', () => ({
    addWorkTaskAttachment: vi.fn(),
    removeWorkTaskAttachment: vi.fn(),
    getWorkTaskSettings: vi.fn(),
    WORK_TASK_SETTINGS_KEY: ['work-tasks', 'settings'],
}))
const api = vi.mocked(await import('../../lib/api'))

const task: WorkTask = {
    id: 1, title: 'Chase notes', description: 'Follow up with the client', departmentId: 1, departmentName: 'Sales',
    projectId: 10, projectName: 'CRM Rollout', projectCode: 'CRM', projectColorKey: 'p1',
    assignees: [{ userId: 'u-sam', displayName: 'Sam Sales' }], createdById: 'u-hr', createdByName: 'Hana HR',
    dueDate: '2020-01-01', targetHours: 10, loggedHours: 4, isBillable: true, priority: 'High', status: 'InProgress',
    createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
    canEdit: true, canChangeStatus: true, attachments: [],
}

function renderDialog(t: WorkTask = task) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={queryClient}>
            <TaskDetailsDialog task={t} today="2026-09-29" onClose={vi.fn()} onEdit={vi.fn()} />
        </QueryClientProvider>,
    )
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getWorkTaskSettings.mockResolvedValue(DEFAULT_TASK_SETTINGS)
})

describe('TaskDetailsDialog', () => {
    it('hides every fact, chip and section the Task Settings hide', async () => {
        api.getWorkTaskSettings.mockResolvedValue({
            ...DEFAULT_TASK_SETTINGS,
            descriptionRequirement: 'Hidden', dueDateRequirement: 'Hidden', targetHoursRequirement: 'Hidden',
            attachmentsRequirement: 'Hidden', billableRequirement: 'Hidden', showPriority: false,
        })
        renderDialog()

        await screen.findByText('Chase notes')
        await waitFor(() => expect(screen.queryByText('Description')).toBeNull())
        expect(screen.queryByText('Due')).toBeNull()
        expect(screen.queryByText('Target')).toBeNull()
        expect(screen.queryByText(/Attachments/)).toBeNull()
        expect(screen.queryByText('Billable')).toBeNull()
        expect(screen.queryByText('High priority')).toBeNull()
        // Created/Completed is not one of the Task Settings' fields — it always shows.
        expect(screen.getByText('Created')).toBeInTheDocument()
    })

    it('still shows facts and chips left visible', async () => {
        renderDialog()

        await screen.findByText('Chase notes')
        expect(screen.getByText('Due')).toBeInTheDocument()
        expect(screen.getByText('Target')).toBeInTheDocument()
        expect(screen.getByText('Description')).toBeInTheDocument()
        expect(screen.getByText('Billable')).toBeInTheDocument()
        expect(screen.getByText('High priority')).toBeInTheDocument()
    })

    it('offers no attach control once attachments are hidden, but keeps files already on the task', async () => {
        api.getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, attachmentsRequirement: 'Hidden' })
        renderDialog({
            ...task,
            attachments: [{
                id: 1, fileName: 'brief.pdf', contentType: 'application/pdf', sizeBytes: 2048,
                url: '/api/files/1', uploadedByName: 'Hana HR', createdAtUtc: '2026-09-01T08:00:00Z', canRemove: false,
            }],
        })

        expect(await screen.findByRole('link', { name: 'brief.pdf' })).toBeInTheDocument()
        // The settings load as "Optional" (today's behaviour) before the mocked
        // "Hidden" response resolves — wait for the real answer to take effect.
        await waitFor(() => expect(screen.queryByRole('button', { name: /Attach files/ })).toBeNull())
    })

    it("refuses a file the settings' configured limits do not allow", async () => {
        api.getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, maxAttachmentSizeMb: 1, allowImages: false })
        renderDialog()

        // Wait for the configured limits, not the defaults every render starts with.
        const input = await screen.findByTestId('task-attachment-input')
        await waitFor(() => expect(input).toHaveAttribute('accept', '.pdf,.doc,.docx,.xls,.xlsx'))
        fireEvent.change(input, { target: { files: [new File([new Uint8Array(10)], 'shot.png')] } })

        expect(await screen.findByRole('alert')).toHaveTextContent('shot.png: only PDF, Word and Excel files can be attached.')
    })
})
