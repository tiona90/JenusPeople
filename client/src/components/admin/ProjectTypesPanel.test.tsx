import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, expect, it, vi } from 'vitest'
import { StoreProvider } from '../../lib/mobx'
import type { ProjectType } from '../../lib/types'
import ProjectTypesPanel from './ProjectTypesPanel'

// Project types are the org-wide catalogue of what kind of engagement a project
// is — Implementation, Support, Internal. The panel is the only place they are
// curated, so what matters here is that the catalogue is legible (rendered,
// searchable, filterable by status) and that an edit sends the whole type back,
// since the API upserts rather than patches.
vi.mock('../../lib/api', () => ({
    getProjectTypes: vi.fn(),
    createProjectType: vi.fn(),
    updateProjectType: vi.fn(),
    deleteProjectType: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

function projectType(id: number, name: string, isActive = true, description = '', usedInProjects = 0): ProjectType {
    return { id, name, description, icon: '🗂️', colorKey: 'default', isActive, usedInProjects }
}

const IMPLEMENTATION = projectType(1, 'Implementation', true, 'New delivery for a customer.', 3)
const SUPPORT = projectType(2, 'Support', true, 'Incidents and small changes.', 1)
const INTERNAL = projectType(3, 'Internal', false, 'Our own products and tooling.')

beforeEach(() => {
    vi.clearAllMocks()
    api.getProjectTypes.mockResolvedValue([IMPLEMENTATION, SUPPORT, INTERNAL])
    api.createProjectType.mockResolvedValue(IMPLEMENTATION)
    api.updateProjectType.mockResolvedValue(IMPLEMENTATION)
})

async function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <ProjectTypesPanel />
            </QueryClientProvider>
        </StoreProvider>,
    )
    await screen.findByPlaceholderText('Search project types…')
    return view
}

it('lists every project type in the catalogue, enabled or not', async () => {
    await renderPanel()

    expect(screen.getByText('Implementation')).toBeInTheDocument()
    expect(screen.getByText('Support')).toBeInTheDocument()
    expect(screen.getByText('Internal')).toBeInTheDocument()
})

it('narrows the catalogue to what matches the search text', async () => {
    await renderPanel()

    fireEvent.change(screen.getByPlaceholderText('Search project types…'), { target: { value: 'suppo' } })

    expect(screen.getByText('Support')).toBeInTheDocument()
    expect(screen.queryByText('Implementation')).not.toBeInTheDocument()
    expect(screen.queryByText('Internal')).not.toBeInTheDocument()
})

/**
 * A disabled type is still part of the catalogue, so it has to be reachable —
 * the status filter is the only way to single one out for re-enabling.
 */
it('filters the catalogue by status', async () => {
    await renderPanel()

    fireEvent.change(screen.getByDisplayValue('All statuses (3)'), { target: { value: 'disabled' } })

    expect(screen.getByText('Internal')).toBeInTheDocument()
    expect(screen.queryByText('Support')).not.toBeInTheDocument()
})

it('creates a project type from the form', async () => {
    await renderPanel()

    fireEvent.click(screen.getByText('+ New project type'))
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Implementation' } })
    fireEvent.change(screen.getByLabelText(/Description/), { target: { value: 'New delivery for a customer.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    // The trailing matcher absorbs the mutation context React Query appends.
    await waitFor(() => expect(api.createProjectType).toHaveBeenCalledWith({
        name: 'Implementation',
        description: 'New delivery for a customer.',
        icon: '🗂️',
        colorKey: 'default',
        isActive: true,
    }, expect.anything()))
})

/**
 * The API refuses to delete a type projects still carry, so the count on the card
 * is what tells an admin why — and how many projects to reclassify first.
 */
it('reports how many projects use each type', async () => {
    await renderPanel()

    // Cards are sorted by name: Implementation, Internal, Support. Support's
    // single project is there for the singular.
    const usage = screen.getAllByText(/Used by/)
        .map((el) => el.textContent?.replace(/\s+/g, ' ').trim())

    expect(usage).toEqual([
        'Used by 3 projects',
        'Used by 0 projects',
        'Used by 1 project',
    ])
})

/**
 * A project carries exactly one type, so the totals add up rather than taking the
 * widest reach the way ComponentsPanel has to.
 */
it('totals the projects classified across every type', async () => {
    await renderPanel()

    expect(screen.getByText('classified by type')).toBeInTheDocument()
    expect(screen.getByText('4')).toBeInTheDocument()
})

/**
 * The toggle is a shortcut for an edit, and the endpoint replaces the type rather
 * than patching it — so everything but IsActive has to be sent back unchanged, or
 * flipping a switch silently blanks the description.
 */
it('sends the rest of the project type unchanged when toggling it off', async () => {
    await renderPanel()

    // The cards are sorted by name, so the first switch is Implementation.
    fireEvent.click(screen.getAllByRole('switch')[0])

    await waitFor(() => expect(api.updateProjectType).toHaveBeenCalledWith(IMPLEMENTATION.id, {
        name: 'Implementation',
        description: 'New delivery for a customer.',
        icon: '🗂️',
        colorKey: 'default',
        isActive: false,
    }))
})
