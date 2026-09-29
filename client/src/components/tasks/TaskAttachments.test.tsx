import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import TaskAttachments, { StagedTaskAttachments } from './TaskAttachments'
import type { WorkTask, WorkTaskAttachment } from '../../lib/types'

vi.mock('../../lib/api', () => ({
    addWorkTaskAttachment: vi.fn(),
    removeWorkTaskAttachment: vi.fn(),
}))
const api = vi.mocked(await import('../../lib/api'))

const brief: WorkTaskAttachment = {
    id: 7, fileName: 'brief.pdf', contentType: 'application/pdf', sizeBytes: 2048, url: '/api/files/abc',
    uploadedByName: 'Hana HR', createdAtUtc: '2026-09-28T08:00:00Z', canRemove: false,
}

function renderLive(attachments: WorkTaskAttachment[], onChanged = vi.fn(), canAttach = true) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={queryClient}>
            <TaskAttachments taskId={3} attachments={attachments} canAttach={canAttach} onChanged={onChanged} />
        </QueryClientProvider>,
    )
    return { onChanged }
}

function pick(...files: File[]) {
    fireEvent.change(screen.getByTestId('task-attachment-input'), { target: { files } })
}

const file = (name: string, size = 500) => new File([new Uint8Array(size)], name)

beforeEach(() => vi.clearAllMocks())

describe('TaskAttachments', () => {
    it('links each file and offers Remove only where the server allows it', () => {
        renderLive([brief, { ...brief, id: 8, fileName: 'mine.png', sizeBytes: 3 * 1024 * 1024, uploadedByName: 'Eve', canRemove: true }])

        expect(screen.getByRole('link', { name: 'brief.pdf' })).toHaveAttribute('href', '/api/files/abc')
        expect(screen.getByText('2 KB · Hana HR')).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Remove brief.pdf' })).toBeNull()
        expect(screen.getByRole('button', { name: 'Remove mine.png' })).toBeInTheDocument()
    })

    it('uploads each picked file and hands back the saved task', async () => {
        const saved = { id: 3, attachments: [brief] } as WorkTask
        api.addWorkTaskAttachment.mockResolvedValue(saved)
        const { onChanged } = renderLive([])

        pick(file('a.pdf'), file('b.xlsx'))

        await waitFor(() => expect(onChanged).toHaveBeenCalledWith(saved))
        expect(api.addWorkTaskAttachment).toHaveBeenCalledTimes(2)
        expect(api.addWorkTaskAttachment).toHaveBeenCalledWith(3, expect.objectContaining({ name: 'a.pdf' }))
    })

    it('refuses up front a file the server would, and sends the rest', async () => {
        api.addWorkTaskAttachment.mockResolvedValue({ id: 3, attachments: [] } as unknown as WorkTask)
        renderLive([])

        pick(file('run.exe'), file('huge.pdf', 11 * 1024 * 1024), file('ok.png'))

        expect(await screen.findByRole('alert')).toHaveTextContent('run.exe: only PDF, Word, Excel, JPG and PNG files can be attached.')
        expect(screen.getByRole('alert')).toHaveTextContent('huge.pdf is larger than the 10MB limit.')
        await waitFor(() => expect(api.addWorkTaskAttachment).toHaveBeenCalledTimes(1))
        expect(api.addWorkTaskAttachment).toHaveBeenCalledWith(3, expect.objectContaining({ name: 'ok.png' }))
    })

    it('offers nothing to attach to somebody who may not edit the task, but still links its files', () => {
        renderLive([brief], vi.fn(), false)

        expect(screen.getByRole('link', { name: 'brief.pdf' })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /Attach files/ })).toBeNull()
        expect(screen.queryByTestId('task-attachment-input')).toBeNull()
    })

    it('stops at ten files', () => {
        renderLive(Array.from({ length: 10 }, (_, i) => ({ ...brief, id: i, fileName: `f${i}.pdf` })))
        expect(screen.getByRole('button', { name: /10 files attached/ })).toBeDisabled()
    })

    it('removes a file', async () => {
        const saved = { id: 3, attachments: [] } as unknown as WorkTask
        api.removeWorkTaskAttachment.mockResolvedValue(saved)
        const { onChanged } = renderLive([{ ...brief, canRemove: true }])

        fireEvent.click(screen.getByRole('button', { name: 'Remove brief.pdf' }))

        await waitFor(() => expect(onChanged).toHaveBeenCalledWith(saved))
        expect(api.removeWorkTaskAttachment).toHaveBeenCalledWith(3, 7)
    })
})

describe('StagedTaskAttachments', () => {
    it('holds picked files without uploading them', () => {
        const onChange = vi.fn()
        render(<StagedTaskAttachments files={[]} onChange={onChange} />)

        pick(file('a.pdf'))

        expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ name: 'a.pdf' })])
        expect(api.addWorkTaskAttachment).not.toHaveBeenCalled()
    })

    it('refuses by the configured limits', async () => {
        const onChange = vi.fn()
        render(
            <StagedTaskAttachments
                files={[]}
                onChange={onChange}
                limits={{ maxFiles: 1, maxBytes: 2 * 1024 * 1024, extensions: ['.pdf'] }}
            />,
        )

        pick(file('big.pdf', 3 * 1024 * 1024), file('shot.png'), file('ok.pdf'))

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('big.pdf is larger than the 2MB limit.')
        expect(alert).toHaveTextContent('shot.png: only PDF files can be attached.')
        expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ name: 'ok.pdf' })])
        expect(screen.getByTestId('task-attachment-input')).toHaveAttribute('accept', '.pdf')
    })
})
