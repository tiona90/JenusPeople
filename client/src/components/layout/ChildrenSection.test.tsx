import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChildrenSection from './ChildrenSection'
import type { Child } from '../../lib/types'

const children: Child[] = [
    { id: 'c1', name: 'Andreas', dateOfBirth: '2019-03-04', ageYears: 7, isEligible: true, lastEligibleDate: '2034-03-03' },
    { id: 'c2', name: 'Petros', dateOfBirth: '2005-01-20', ageYears: 21, isEligible: false, lastEligibleDate: '2020-01-19' },
]

const getChildren = vi.fn()
const createChild = vi.fn()
const deleteChild = vi.fn()

vi.mock('../../lib/api', () => ({
    getChildren: (...args: unknown[]) => getChildren(...args),
    createChild: (...args: unknown[]) => createChild(...args),
    updateChild: vi.fn(),
    deleteChild: (...args: unknown[]) => deleteChild(...args),
}))

function renderSection(hasChildren: boolean | null = true) {
    const onHasChildrenChange = vi.fn()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={client}>
            <ChildrenSection hasChildren={hasChildren} onHasChildrenChange={onHasChildrenChange} />
        </QueryClientProvider>,
    )
    return { onHasChildrenChange }
}

describe('ChildrenSection', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        getChildren.mockResolvedValue(children)
        createChild.mockResolvedValue(children[0])
        deleteChild.mockResolvedValue(undefined)
    })

    it('lists each child with their computed age', async () => {
        renderSection()

        expect(await screen.findByText('Andreas')).toBeInTheDocument()
        expect(screen.getByText(/7/)).toBeInTheDocument()
        expect(screen.getByText('Petros')).toBeInTheDocument()
    })

    /**
     * An aged-out child is shown, not hidden: their leave is still history, and a
     * disappearing row reads as data loss.
     */
    it('marks a child over the age limit as no longer eligible', async () => {
        renderSection()

        await screen.findByText('Petros')
        expect(screen.getByText(/no longer eligible/i)).toBeInTheDocument()
    })

    it('hides the list when the employee says they have no children', async () => {
        renderSection(false)

        await waitFor(() => expect(screen.queryByText('Andreas')).not.toBeInTheDocument())
        // The absence of the row alone doesn't prove the fetch was skipped — the whole
        // list sits behind `{declared && (...)}`, so it would be hidden either way.
        // This is the assertion that actually pins `enabled: declared` on the query.
        expect(getChildren).not.toHaveBeenCalled()
    })

    it('adds a child', async () => {
        renderSection()
        await screen.findByText('Andreas')

        fireEvent.click(screen.getByRole('button', { name: /add child/i }))
        fireEvent.change(screen.getByLabelText(/child's name/i), { target: { value: 'Maria' } })
        fireEvent.change(screen.getByLabelText(/date of birth/i), { target: { value: '2022-09-12' } })
        fireEvent.click(screen.getByRole('button', { name: /^save child$/i }))

        await waitFor(() =>
            expect(createChild).toHaveBeenCalledWith({ name: 'Maria', dateOfBirth: '2022-09-12' }),
        )
    })

    /**
     * Ticking the box is the parent's business — it saves with the profile — so the
     * section only reports the change upward.
     */
    it('reports a change to the declaration rather than saving it', async () => {
        const { onHasChildrenChange } = renderSection(false)

        fireEvent.click(screen.getByRole('checkbox', { name: /i have children/i }))

        expect(onHasChildrenChange).toHaveBeenCalledWith(true)
    })
})
