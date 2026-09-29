import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import TaskSettingsPanel from './TaskSettingsPanel'
import { DEFAULT_TASK_SETTINGS } from '../../lib/task-settings'

const getWorkTaskSettings = vi.fn()
const updateWorkTaskSettings = vi.fn()
vi.mock('../../lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('../../lib/api')>()),
    getWorkTaskSettings: () => getWorkTaskSettings(),
    updateWorkTaskSettings: (s: unknown) => updateWorkTaskSettings(s),
}))

function renderPanel() {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(<QueryClientProvider client={client}><TaskSettingsPanel /></QueryClientProvider>)
}

describe('TaskSettingsPanel', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        getWorkTaskSettings.mockResolvedValue(DEFAULT_TASK_SETTINGS)
        updateWorkTaskSettings.mockImplementation(async (s) => s)
    })

    it('offers each field only its allowed values', async () => {
        renderPanel()
        const project = await screen.findByRole('group', { name: 'Project' })
        expect(within(project).queryByRole('button', { name: 'Hidden' })).toBeNull()
        const billable = screen.getByRole('group', { name: 'Billable' })
        expect(within(billable).queryByRole('button', { name: 'Optional' })).toBeNull()
        const due = screen.getByRole('group', { name: 'Due date' })
        expect(within(due).getAllByRole('button')).toHaveLength(3)
    })

    it('saves the whole settings object', async () => {
        renderPanel()
        const due = await screen.findByRole('group', { name: 'Due date' })
        fireEvent.click(within(due).getByRole('button', { name: 'Required' }))
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

        await waitFor(() => expect(updateWorkTaskSettings).toHaveBeenCalledWith({ ...DEFAULT_TASK_SETTINGS, dueDateRequirement: 'Required' }))
    })

    it('says what switching confirmation off does', async () => {
        renderPanel()
        expect(await screen.findByText(/closes every task now waiting for confirmation/i)).toBeInTheDocument()
    })

    it('holds Save when no file kind is allowed', async () => {
        renderPanel()
        for (const kind of ['Images', 'PDF', 'Word', 'Excel'])
            fireEvent.click(await screen.findByRole('checkbox', { name: kind }))
        expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
        expect(screen.getByText('Allow at least one kind of file.')).toBeInTheDocument()
    })
})
