import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, expect, it, vi } from 'vitest'
import { StoreProvider } from '../../lib/mobx'
import type { ProjectComponent } from '../../lib/types'
import ComponentsPanel from './ComponentsPanel'

// Components are the org-wide catalogue of deliverables a project is made of —
// DM, Lasernet, jDocs. The panel is the only place they are curated, so what
// matters here is that the catalogue is legible (rendered, searchable,
// filterable by status) and that an edit sends the whole component back, since
// the API upserts rather than patches.
vi.mock('../../lib/api', () => ({
    getProjectComponents: vi.fn(),
    createProjectComponent: vi.fn(),
    updateProjectComponent: vi.fn(),
    deleteProjectComponent: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

function component(id: number, name: string, isActive = true, description = '', usedInProjects = 0): ProjectComponent {
    return { id, name, description, icon: '🧩', colorKey: 'default', isActive, usedInProjects }
}

const DM = component(1, 'DM', true, 'Data management.', 3)
const LASERNET = component(2, 'Lasernet', true, 'Document output.', 1)
const JDOCS = component(3, 'jDocs', false, 'Document generation.')

beforeEach(() => {
    vi.clearAllMocks()
    api.getProjectComponents.mockResolvedValue([DM, LASERNET, JDOCS])
    api.createProjectComponent.mockResolvedValue(DM)
    api.updateProjectComponent.mockResolvedValue(DM)
})

async function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <ComponentsPanel />
            </QueryClientProvider>
        </StoreProvider>,
    )
    await screen.findByPlaceholderText('Search components…')
    return view
}

it('lists every component in the catalogue, enabled or not', async () => {
    await renderPanel()

    expect(screen.getByText('DM')).toBeInTheDocument()
    expect(screen.getByText('Lasernet')).toBeInTheDocument()
    expect(screen.getByText('jDocs')).toBeInTheDocument()
})

it('narrows the catalogue to what matches the search text', async () => {
    await renderPanel()

    fireEvent.change(screen.getByPlaceholderText('Search components…'), { target: { value: 'laser' } })

    expect(screen.getByText('Lasernet')).toBeInTheDocument()
    expect(screen.queryByText('DM')).not.toBeInTheDocument()
    expect(screen.queryByText('jDocs')).not.toBeInTheDocument()
})

/**
 * A disabled component is still part of the catalogue, so it has to be reachable
 * — the status filter is the only way to single one out for re-enabling.
 */
it('filters the catalogue by status', async () => {
    await renderPanel()

    fireEvent.change(screen.getByDisplayValue('All statuses (3)'), { target: { value: 'disabled' } })

    expect(screen.getByText('jDocs')).toBeInTheDocument()
    expect(screen.queryByText('DM')).not.toBeInTheDocument()
})

it('creates a component from the form', async () => {
    await renderPanel()

    fireEvent.click(screen.getByText('+ New component'))
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Lasernet' } })
    fireEvent.change(screen.getByLabelText(/Description/), { target: { value: 'Document output.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    // The trailing matcher absorbs the mutation context React Query appends.
    await waitFor(() => expect(api.createProjectComponent).toHaveBeenCalledWith({
        name: 'Lasernet',
        description: 'Document output.',
        icon: '🧩',
        colorKey: 'default',
        isActive: true,
    }, expect.anything()))
})

/**
 * Deleting a component is never refused — its project assignments cascade away
 * with it — so the count on the card is the only warning an admin gets that
 * removing one changes projects.
 */
it('reports how many projects declare each component', async () => {
    await renderPanel()

    // Cards are sorted by name: DM, jDocs, Lasernet. Lasernet's single project
    // is there for the singular.
    const usage = screen.getAllByText(/Declared by/)
        .map((el) => el.textContent?.replace(/\s+/g, ' ').trim())

    expect(usage).toEqual([
        'Declared by 3 projects',
        'Declared by 0 projects',
        'Declared by 1 project',
    ])
})

/**
 * The toggle is a shortcut for an edit, and the endpoint replaces the component
 * rather than patching it — so everything but IsActive has to be sent back
 * unchanged, or flipping a switch silently blanks the description.
 */
it('sends the rest of the component unchanged when toggling it off', async () => {
    await renderPanel()

    // The cards are sorted by name, so the first switch is DM.
    fireEvent.click(screen.getAllByRole('switch')[0])

    await waitFor(() => expect(api.updateProjectComponent).toHaveBeenCalledWith(DM.id, {
        name: 'DM',
        description: 'Data management.',
        icon: '🧩',
        colorKey: 'default',
        isActive: false,
    }))
})
