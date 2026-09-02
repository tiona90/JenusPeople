import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { StoreProvider } from '../../lib/mobx'
import type { AdminUser, Department, Project, ProjectActivityType, ProjectComponent, ProjectType } from '../../lib/types'
import ProjectsPanel from './ProjectsPanel'

// Activity types are an org-wide catalogue. A project picks the subset it logs
// time against, so the timesheet dropdown can be narrowed to the work that
// project actually does — these cover the picking, in the project dialog.
vi.mock('../../lib/api', () => ({
    getProjects: vi.fn(),
    getDepartments: vi.fn(),
    getAdminUsers: vi.fn(),
    getProjectActivityTypes: vi.fn(),
    getProjectComponents: vi.fn(),
    getProjectTypes: vi.fn(),
    createProject: vi.fn(),
    updateProject: vi.fn(),
    deleteProject: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

function activityType(id: number, name: string, isActive = true): ProjectActivityType {
    return { id, name, description: '', icon: '🏷️', colorKey: 'default', isActive, hoursYtd: 0, usedInProjects: 0 }
}

const DEVELOPMENT = activityType(1, 'Development')
const TESTING = activityType(2, 'Testing')
const DESIGN = activityType(3, 'Design')
const RETIRED = activityType(4, 'Retired Activity', false)

// Components work the same way: an org-wide catalogue a project narrows to the
// deliverables it is made up of.
function component(id: number, name: string, isActive = true): ProjectComponent {
    return { id, name, description: '', icon: '🧩', colorKey: 'default', isActive, usedInProjects: 0 }
}

const DM = component(11, 'DM')
const LASERNET = component(12, 'Lasernet')
const JDOCS = component(13, 'jDocs')
const RETIRED_COMPONENT = component(14, 'Retired Component', false)

// Types work like components: a project carries any number of them, picked from
// the catalogue, and may carry none at all.
function projectType(id: number, name: string, isActive = true): ProjectType {
    return { id, name, description: '', icon: '🗂️', colorKey: 'default', isActive, usedInProjects: 0 }
}

const IMPLEMENTATION = projectType(21, 'Implementation')
const SUPPORT = projectType(22, 'Support')
const RETIRED_TYPE = projectType(23, 'Retired Type', false)

const ENGINEERING: Department = { id: 1, name: 'Engineering', code: 'ENG', isActive: true, createdAt: '2026-01-01T00:00:00' }
const FINANCE: Department = { id: 2, name: 'Finance', code: 'FIN', isActive: true, createdAt: '2026-01-01T00:00:00' }

const APOLLO: Project = {
    id: 7,
    name: 'Apollo',
    code: 'APL-001',
    description: '',
    isActive: true,
    status: 'Active',
    departments: [{ id: 1, name: 'Engineering' }],
    ownerId: null,
    ownerName: null,
    colorKey: 'p1',
    targetWeeklyHours: 0,
    targetMonthlyHours: 0,
    createdAt: '2026-01-01T00:00:00',
    hoursThisWeek: 0,
    hoursThisMonth: 0,
    hoursYTD: 0,
    teamSize: 0,
    team: [],
    activities: [{ id: 2, name: 'Testing', icon: '🏷️', colorKey: 'default' }],
    components: [{ id: 12, name: 'Lasernet', icon: '🧩', colorKey: 'default' }],
    types: [
        { id: 21, name: 'Implementation', icon: '🗂️', colorKey: 'default' },
        { id: 22, name: 'Support', icon: '🗂️', colorKey: 'default' },
    ],
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getProjects.mockResolvedValue([APOLLO])
    api.getDepartments.mockResolvedValue([ENGINEERING, FINANCE])
    api.getAdminUsers.mockResolvedValue([] as AdminUser[])
    api.getProjectActivityTypes.mockResolvedValue([DEVELOPMENT, TESTING, DESIGN, RETIRED])
    api.getProjectComponents.mockResolvedValue([DM, LASERNET, JDOCS, RETIRED_COMPONENT])
    api.getProjectTypes.mockResolvedValue([IMPLEMENTATION, SUPPORT, RETIRED_TYPE])
    api.createProject.mockResolvedValue(APOLLO)
    api.updateProject.mockResolvedValue(APOLLO)
})

async function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <ProjectsPanel />
            </QueryClientProvider>
        </StoreProvider>,
    )
    await screen.findByPlaceholderText('Search projects…')
    return view
}

/** The Activities multi-select inside the open dialog. */
function activitiesField() {
    return within(screen.getByRole('dialog')).getByLabelText('Activities')
}

/** Opens the multi-select and returns the option elements by label. */
function openActivityOptions() {
    fireEvent.mouseDown(activitiesField())
    return screen.getAllByRole('option')
}

function chooseActivity(name: string) {
    fireEvent.click(screen.getByRole('option', { name: new RegExp(name) }))
}

/** The Components multi-select inside the open dialog. */
function componentsField() {
    return within(screen.getByRole('dialog')).getByLabelText('Components')
}

function openComponentOptions() {
    fireEvent.mouseDown(componentsField())
    return screen.getAllByRole('option')
}

function chooseComponent(name: string) {
    fireEvent.click(screen.getByRole('option', { name: new RegExp(name) }))
}

/** The Departments multi-select inside the open dialog. */
function departmentsField() {
    return within(screen.getByRole('dialog')).getByLabelText(/Departments/)
}

function chooseDepartment(name: string) {
    fireEvent.mouseDown(departmentsField())
    fireEvent.click(screen.getByRole('option', { name: new RegExp(name) }))
    fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })
}

function save() {
    fireEvent.click(within(screen.getByRole('dialog')).getByText('Save'))
}

describe('ProjectsPanel — project activities', () => {
    it('offers the active activity types when creating a project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        const labels = openActivityOptions().map((o) => o.textContent)

        expect(labels).toEqual(['Development', 'Testing', 'Design'])
        expect(labels).not.toContain('Retired Activity')
    })

    it('sends the chosen activities when saving a new project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Name/), {
            target: { value: 'Borealis' },
        })
        // Required for the dialog to save at all; the assertion below is still
        // about the activities.
        chooseDepartment('Engineering')
        openActivityOptions()
        chooseActivity('Development')
        chooseActivity('Design')
        fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })

        fireEvent.click(within(screen.getByRole('dialog')).getByText('Save'))

        await waitFor(() => expect(api.createProject).toHaveBeenCalled())
        expect(api.createProject.mock.calls[0][0].activityTypeIds).toEqual([1, 3])
    })

    it('preselects the activities a project already has when editing', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        expect(activitiesField()).toHaveTextContent('Testing')
    })

    it('keeps the existing selection when saving an edit untouched', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        fireEvent.click(within(screen.getByRole('dialog')).getByText('Save'))

        await waitFor(() => expect(api.updateProject).toHaveBeenCalled())
        expect(api.updateProject.mock.calls[0][1].activityTypeIds).toEqual([2])
    })
})

// Components are the deliverables a project is made up of, picked from the
// org-wide catalogue the same way activities are. Unlike activities there is no
// fallback: an empty selection means the project declares none.
describe('ProjectsPanel — project components', () => {
    it('offers the active components when creating a project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        const labels = openComponentOptions().map((o) => o.textContent)

        expect(labels).toEqual(['DM', 'Lasernet', 'jDocs'])
        expect(labels).not.toContain('Retired Component')
    })

    it('sends the chosen components when saving a new project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Name/), {
            target: { value: 'Borealis' },
        })
        chooseDepartment('Engineering')
        openComponentOptions()
        chooseComponent('DM')
        chooseComponent('jDocs')
        fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })

        save()

        await waitFor(() => expect(api.createProject).toHaveBeenCalled())
        expect(api.createProject.mock.calls[0][0].componentIds).toEqual([11, 13])
    })

    it('preselects the components a project already has when editing', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        expect(componentsField()).toHaveTextContent('Lasernet')
    })

    it('keeps the existing selection when saving an edit untouched', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        save()

        await waitFor(() => expect(api.updateProject).toHaveBeenCalled())
        expect(api.updateProject.mock.calls[0][1].componentIds).toEqual([12])
    })

    // Every other field keeps its value, so clearing components has to reach the
    // API as an empty list rather than being dropped from the payload.
    it('sends an empty list when the selection is cleared', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        openComponentOptions()
        chooseComponent('Lasernet')
        fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })

        save()

        await waitFor(() => expect(api.updateProject).toHaveBeenCalled())
        expect(api.updateProject.mock.calls[0][1].componentIds).toEqual([])
    })
})
// A project carries a set of types, or none — the same multi-select shape as the
// components above. Clearing it back to empty has to reach the API as an empty
// list, since staying unclassified is a valid state a project can be edited into.
describe('ProjectsPanel — project types', () => {
    /** The Project types multi-select inside the open dialog. */
    function typeField() {
        return within(screen.getByRole('dialog')).getByLabelText('Project types')
    }

    function openTypeOptions() {
        fireEvent.mouseDown(typeField())
        return screen.getAllByRole('option')
    }

    function chooseType(name: string) {
        fireEvent.click(screen.getByRole('option', { name: new RegExp(name) }))
    }

    it('offers the active types when creating a project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        const labels = openTypeOptions().map((o) => o.textContent)

        expect(labels).toEqual(['Implementation', 'Support'])
        expect(labels).not.toContain('Retired Type')
    })

    it('sends the chosen types when saving a new project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Name/), {
            target: { value: 'Borealis' },
        })
        chooseDepartment('Engineering')
        openTypeOptions()
        chooseType('Implementation')
        chooseType('Support')
        fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })

        save()

        await waitFor(() => expect(api.createProject).toHaveBeenCalled())
        expect(api.createProject.mock.calls[0][0].projectTypeIds).toEqual([21, 22])
    })

    /**
     * Leaving the select alone is the common case — a project need not be
     * classified — and it has to reach the API as an empty list rather than
     * being dropped from the payload.
     */
    it('sends an empty list when no type is chosen', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Name/), {
            target: { value: 'Borealis' },
        })
        chooseDepartment('Engineering')

        save()

        await waitFor(() => expect(api.createProject).toHaveBeenCalled())
        expect(api.createProject.mock.calls[0][0].projectTypeIds).toEqual([])
    })

    it('preselects the types a project already has when editing', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        expect(typeField()).toHaveTextContent('Implementation')
        expect(typeField()).toHaveTextContent('Support')
    })

    it('keeps the existing selection when saving an edit untouched', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        save()

        await waitFor(() => expect(api.updateProject).toHaveBeenCalled())
        expect(api.updateProject.mock.calls[0][1].projectTypeIds).toEqual([21, 22])
    })

    /**
     * Clearing the selection has to unclassify the project rather than being
     * dropped from the payload, which would silently leave the old types on it.
     */
    it('sends an empty list when the selection is cleared', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        openTypeOptions()
        chooseType('Implementation')
        chooseType('Support')
        fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })

        save()

        await waitFor(() => expect(api.updateProject).toHaveBeenCalled())
        expect(api.updateProject.mock.calls[0][1].projectTypeIds).toEqual([])
    })

    it('badges the project card with every type it carries', async () => {
        await renderPanel()

        expect(await screen.findByTitle('Project type: Support')).toBeInTheDocument()
        expect(screen.getByTitle('Project type: Implementation')).toBeInTheDocument()
    })
})

// A project's departments decide who can see it, so an empty selection would
// produce a project nobody but an admin can reach. The dialog refuses to make
// one, which is the front half of the rule the API validator enforces.
describe('ProjectsPanel — project departments', () => {
    it('sends the chosen departments when saving a new project', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Name/), {
            target: { value: 'Borealis' },
        })
        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Code/), {
            target: { value: 'BOR-002' },
        })
        chooseDepartment('Engineering')
        chooseDepartment('Finance')

        save()

        await waitFor(() => expect(api.createProject).toHaveBeenCalled())
        expect(api.createProject.mock.calls[0][0].departmentIds).toEqual([1, 2])
    })

    it('refuses to save a project with no department', async () => {
        await renderPanel()
        fireEvent.click(screen.getByText('+ New project'))

        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Name/), {
            target: { value: 'Borealis' },
        })
        fireEvent.change(within(screen.getByRole('dialog')).getByLabelText(/Code/), {
            target: { value: 'BOR-002' },
        })

        save()

        expect(api.createProject).not.toHaveBeenCalled()
        expect(within(screen.getByRole('dialog')).getByText(/at least one department/i)).toBeTruthy()
    })

    it('preselects the departments a project already has when editing', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        expect(departmentsField()).toHaveTextContent('Engineering')
    })

    it('keeps the existing selection when saving an edit untouched', async () => {
        await renderPanel()
        fireEvent.click(await screen.findByText('✏️ Edit'))

        save()

        await waitFor(() => expect(api.updateProject).toHaveBeenCalled())
        expect(api.updateProject.mock.calls[0][1].departmentIds).toEqual([1])
    })
})
