import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Project, ProjectActivityType, ProjectComponent, ProjectType, TimesheetTaskOption, UserInfo } from '../../lib/types'
import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import NewTimesheetPage from './NewTimesheetPage'

vi.mock('../../lib/api')
// The attendance hooks import their API module directly, not through the index.
vi.mock('../../lib/api/attendance')
vi.mock('../../lib/mobx')

const api = vi.mocked(await import('../../lib/api'))
const attendanceApi = vi.mocked(await import('../../lib/api/attendance'))
const mobx = vi.mocked(await import('../../lib/mobx'))

/* ─── catalogues ─────────────────────────────────────────────────────────── */

const type = (id: number, name: string): ProjectType => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 })
const component = (id: number, name: string): ProjectComponent => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 })
const activity = (id: number, name: string): ProjectActivityType => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 } as ProjectActivityType)
const summary = (x: { id: number; name: string }) => ({ id: x.id, name: x.name, icon: '', colorKey: 'default' })

const SUPPORT = type(1, 'Support')
const ISSUE = type(2, 'Issue')
const DM = component(1, 'DM')
const JDOCS = component(2, 'jDocs')
const DEV = activity(1, 'Development')
const REVIEW = activity(2, 'Review')

function project(id: number, name: string, over: Partial<Project> = {}): Project {
    return { id, name, code: name.slice(0, 3).toUpperCase(), isActive: true, types: [], components: [], activities: [], ...over } as Project
}
// Apollo leaves one type and one component, two activities.
const APOLLO = project(10, 'Apollo', { types: [summary(SUPPORT)], components: [summary(DM)], activities: [summary(DEV), summary(REVIEW)] })
// Borealis carries both types and declares no component or activity: the whole catalogue of each.
const BOREALIS = project(11, 'Borealis', { types: [summary(SUPPORT), summary(ISSUE)] })
// Cassini is unclassified and made of jDocs alone.
const CASSINI = project(12, 'Cassini', { components: [summary(JDOCS)] })

/* ─── dates: local calendar parts, never toISOString ─────────────────────── */

function iso(d: Date) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
function thisWeek() {
    const today = new Date()
    today.setHours(0, 0, 0, 0)
    const monday = new Date(today)
    const dow = monday.getDay()
    monday.setDate(monday.getDate() + (dow === 0 ? -6 : 1 - dow))
    const sunday = new Date(monday)
    sunday.setDate(sunday.getDate() + 6)
    return { today: iso(today), monday: iso(monday), sunday: iso(sunday) }
}

/** A Draft for this week with one entry on today's date, so today's open card shows that row. */
export function sheetWith(entry: Partial<TimesheetEntry>): Timesheet {
    const { today, monday, sunday } = thisWeek()
    const row: TimesheetEntry = {
        id: 'e1', timesheetId: 'ts-1', projectId: 10, date: `${today}T00:00:00`, hoursWorked: 2, notes: null,
        projectTypeId: null, projectComponentId: null, activityTypeId: null, workTaskId: null, ...entry,
    }
    return {
        id: 'ts-1', employeeId: 'p1', employeeName: 'Me', departmentId: 1, status: 'Draft',
        periodStart: `${monday}T00:00:00`, periodEnd: `${sunday}T00:00:00`, totalHours: 2,
        submittedAt: null, approvedAt: null, createdAt: `${monday}T08:00:00`, projectSummaries: [], dailyHours: [], entries: [row],
    } as Timesheet
}

/* ─── render ─────────────────────────────────────────────────────────────── */

interface Setup {
    projects?: Project[]
    types?: ProjectType[]
    components?: ProjectComponent[]
    activities?: ProjectActivityType[]
    taskOptions?: TimesheetTaskOption[]
    sheet?: Timesheet
}

async function renderPage(s: Setup = {}) {
    mobx.useStore.mockReturnValue({ uiStore: { consumePendingWeekStart: () => null, navigateToTimesheets: vi.fn() } } as never)
    api.getProjects.mockResolvedValue(s.projects ?? [APOLLO, BOREALIS, CASSINI])
    api.getProjectTypes.mockResolvedValue(s.types ?? [SUPPORT, ISSUE])
    api.getProjectComponents.mockResolvedValue(s.components ?? [DM, JDOCS])
    api.getProjectActivityTypes.mockResolvedValue(s.activities ?? [DEV, REVIEW])
    api.getTimesheetTaskOptions.mockResolvedValue(s.taskOptions ?? [])
    api.getMyTimesheets.mockResolvedValue(s.sheet ? [s.sheet] : [])
    if (s.sheet) api.getTimesheet.mockResolvedValue(s.sheet)
    api.createTimesheet.mockResolvedValue({ id: 'ts-new' } as Timesheet)
    api.createTimesheetEntry.mockResolvedValue({} as TimesheetEntry)
    attendanceApi.getAttendanceToday.mockResolvedValue({ status: 'out' } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={queryClient}>
            <NewTimesheetPage user={{ id: 'u1', roles: ['Employee'] } as UserInfo} />
        </QueryClientProvider>,
    )
    // Today's card opens by itself; its one row is the row under test. The columns
    // follow the catalogues, so wait for every query to land before asserting.
    await screen.findByRole('combobox', { name: 'Project' })
    await waitFor(() => expect(queryClient.isFetching()).toBe(0))
}

const combo = (name: string) => screen.getByRole('combobox', { name })

export async function pick(name: string, option: string | RegExp) {
    fireEvent.mouseDown(combo(name))
    fireEvent.click(await screen.findByRole('option', { name: option }))
}

/** The header strip of the one open day card: the column captions in order. */
function headerLabels() {
    const row = combo('Project').closest('[data-testid="entry-row"]')!
    const strip = row.parentElement!.previousElementSibling!
    return Array.from(strip.children).map((el) => el.textContent)
}

beforeEach(() => {
    vi.clearAllMocks()
})

describe('NewTimesheetPage — which columns are asked', () => {
    it('asks Type, Component and Activity when each catalogue has two or more entries', async () => {
        await renderPage()
        expect(combo('Type')).toBeInTheDocument()
        expect(combo('Component')).toBeInTheDocument()
        expect(combo('Activity')).toBeInTheDocument()
        expect(headerLabels()).toEqual(['Task', 'Project', 'Type', 'Component', 'Activity', 'Hours', ''])
    })

    it('drops a column whose catalogue is empty, header and picker together', async () => {
        await renderPage({ components: [], activities: [] })
        expect(screen.queryByRole('combobox', { name: 'Component' })).toBeNull()
        expect(screen.queryByRole('combobox', { name: 'Activity' })).toBeNull()
        expect(combo('Type')).toBeInTheDocument()
        expect(headerLabels()).toEqual(['Task', 'Project', 'Type', 'Hours', ''])
    })

    it('drops a column whose catalogue has one entry — there is nothing to choose', async () => {
        await renderPage({ components: [DM] })
        expect(screen.queryByRole('combobox', { name: 'Component' })).toBeNull()
        expect(headerLabels()).toEqual(['Task', 'Project', 'Type', 'Activity', 'Hours', ''])
    })

    it('leaves a row of Task, Project and Hours when no catalogue is in use', async () => {
        await renderPage({ types: [], components: [], activities: [] })
        expect(screen.getAllByRole('combobox').map((c) => c.getAttribute('aria-label'))).toEqual(['Task', 'Project'])
        expect(headerLabels()).toEqual(['Task', 'Project', 'Hours', ''])
    })
})

describe('NewTimesheetPage — a single option is filled in', () => {
    it('fills and locks what the project leaves one answer for, and leaves the rest to choose', async () => {
        await renderPage()
        await pick('Project', 'Apollo')

        expect(combo('Type')).toHaveTextContent('Support')
        expect(combo('Type')).toHaveAttribute('aria-disabled', 'true')
        expect(combo('Component')).toHaveTextContent('DM')
        expect(combo('Component')).toHaveAttribute('aria-disabled', 'true')
        // Two activities: still a choice.
        expect(combo('Activity')).not.toHaveAttribute('aria-disabled', 'true')
        expect(combo('Activity')).toHaveTextContent('Activity…')
    })

    it('saves what it filled in', async () => {
        await renderPage()
        await pick('Project', 'Apollo')
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '2' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))

        await waitFor(() => expect(api.createTimesheetEntry).toHaveBeenCalledTimes(1))
        expect(api.createTimesheetEntry.mock.calls[0][1]).toEqual(expect.objectContaining({
            projectId: 10, projectTypeId: 1, projectComponentId: 1, activityTypeId: null, hoursWorked: 2,
        }))
    })

    it('leaves a project with several options to choose, unlocked and blank', async () => {
        await renderPage()
        await pick('Project', 'Borealis')
        expect(combo('Type')).not.toHaveAttribute('aria-disabled', 'true')
        expect(combo('Type')).toHaveTextContent('Any type…')
        expect(combo('Component')).not.toHaveAttribute('aria-disabled', 'true')
    })

    it('writes a hidden single-entry catalogue silently', async () => {
        await renderPage({ components: [DM] })
        expect(screen.queryByRole('combobox', { name: 'Component' })).toBeNull()
        await pick('Project', 'Borealis')
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '1' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))

        await waitFor(() => expect(api.createTimesheetEntry).toHaveBeenCalledTimes(1))
        expect(api.createTimesheetEntry.mock.calls[0][1]).toEqual(expect.objectContaining({ projectId: 11, projectComponentId: 1 }))
    })

    it('fills in after a task sets the project', async () => {
        await renderPage({ taskOptions: [{ id: 7, title: 'Fix login', projectId: 10, projectCode: 'APO', targetHours: null, loggedHours: 0, isClosed: false }] })
        await pick('Task', /Fix login/)
        expect(combo('Project')).toHaveTextContent('Apollo')
        expect(combo('Type')).toHaveTextContent('Support')
        expect(combo('Type')).toHaveAttribute('aria-disabled', 'true')
        expect(combo('Component')).toHaveTextContent('DM')
    })

    it('keeps a filled value the next project still offers, and refills when it does not', async () => {
        // Cassini first: unclassified, so no type is filled and the Project picker keeps listing every project.
        await renderPage()
        await pick('Project', 'Cassini')
        expect(combo('Component')).toHaveTextContent('jDocs')
        expect(combo('Component')).toHaveAttribute('aria-disabled', 'true')

        // Borealis declares none, so both components are offered: jDocs stays, unlocked.
        await pick('Project', 'Borealis')
        expect(combo('Component')).toHaveTextContent('jDocs')
        expect(combo('Component')).not.toHaveAttribute('aria-disabled', 'true')

        // Apollo is made of DM alone: jDocs goes, DM is filled in and locked.
        await pick('Project', 'Apollo')
        expect(combo('Component')).toHaveTextContent('DM')
        expect(combo('Component')).toHaveAttribute('aria-disabled', 'true')
    })

    it('never overwrites a choice already made', async () => {
        await renderPage()
        await pick('Project', 'Borealis')
        await pick('Component', 'jDocs')
        // Borealis → Apollo: Apollo offers DM only, so jDocs is dropped by retention and DM filled — that is the
        // retention rule, not an overwrite. Pick Borealis again: DM survives (offered) and is not replaced.
        await pick('Project', 'Apollo')
        expect(combo('Component')).toHaveTextContent('DM')
        await pick('Project', 'Borealis')
        expect(combo('Component')).toHaveTextContent('DM')
    })
})

describe('NewTimesheetPage — a stored value the project no longer offers', () => {
    it('still reads, and the picker stays editable rather than locking onto the one option', async () => {
        // Apollo carries Support only; this row was saved as Issue.
        await renderPage({ sheet: sheetWith({ projectTypeId: 2 }) })
        expect(combo('Type')).toHaveTextContent('Issue')
        expect(combo('Type')).not.toHaveAttribute('aria-disabled', 'true')
        // The Project picker narrows by type, and Apollo does not carry Issue — the row's own project must still read.
        expect(combo('Project')).toHaveTextContent('Apollo')
    })

    it('names a stored component the project does not declare', async () => {
        // Apollo is made of DM; this row was saved against jDocs.
        await renderPage({ sheet: sheetWith({ projectComponentId: 2 }) })
        expect(combo('Component')).toHaveTextContent('jDocs')
    })

    it('does not fill a loaded row — blanks stay as they were saved', async () => {
        await renderPage({ sheet: sheetWith({}) })
        expect(combo('Type')).toHaveTextContent('Any type…')
        expect(combo('Component')).toHaveTextContent('Component…')
    })
})

describe('NewTimesheetPage — rows are entries', () => {
    it('calls a row an entry, not a task, which is the picker beside it', async () => {
        await renderPage()
        expect(screen.getByRole('button', { name: '+ Add entry' })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: '+ Add task' })).toBeNull()

        // Hours with no project: the row is flagged and the message names entries.
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '2' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))
        expect(await screen.findByText('Please fix the highlighted entries before saving.')).toBeInTheDocument()
        expect(screen.getByText('Select a project for this entry.')).toBeInTheDocument()
    })
})

describe('NewTimesheetPage — a type nobody chose does not stand in the way', () => {
    it('lets a mispicked project be changed to an unclassified one, dropping the type filled in for the first', async () => {
        await renderPage()
        await pick('Project', 'Apollo')
        expect(combo('Type')).toHaveTextContent('Support')

        // Cassini is unclassified; a chosen type would hide it, a filled-in one must not.
        await pick('Project', 'Cassini')
        expect(combo('Project')).toHaveTextContent('Cassini')
        expect(combo('Type')).toHaveTextContent('Any type…')
        expect(combo('Component')).toHaveTextContent('jDocs')
    })

    it('does the same when the Type column is hidden, and saves no type for the unclassified project', async () => {
        await renderPage({ types: [SUPPORT] })
        expect(screen.queryByRole('combobox', { name: 'Type' })).toBeNull()
        await pick('Project', 'Apollo')
        await pick('Project', 'Cassini')
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '1' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))

        await waitFor(() => expect(api.createTimesheetEntry).toHaveBeenCalledTimes(1))
        expect(api.createTimesheetEntry.mock.calls[0][1]).toEqual(expect.objectContaining({ projectId: 12, projectTypeId: null }))
    })

    it('keeps a type the user did choose when the next project carries it', async () => {
        await renderPage()
        await pick('Project', 'Borealis')
        await pick('Type', 'Support')
        await pick('Project', 'Apollo')
        expect(combo('Type')).toHaveTextContent('Support')
    })
})

describe('NewTimesheetPage — a task a Manager or HR closed', () => {
    const open: TimesheetTaskOption = { id: 7, title: 'Fix login', projectId: 10, projectCode: 'APO', targetHours: null, loggedHours: 0, isClosed: false }
    const done: TimesheetTaskOption = { id: 8, title: 'Old report', projectId: 10, projectCode: 'APO', targetHours: null, loggedHours: 3, isClosed: true }

    it('is not offered to a row that does not already name it', async () => {
        await renderPage({ taskOptions: [open, done] })
        fireEvent.mouseDown(combo('Task'))
        expect(await screen.findByRole('option', { name: /Fix login/ })).toBeInTheDocument()
        expect(screen.queryByRole('option', { name: /Old report/ })).toBeNull()
    })

    it('still reads on a row already logged against it, but cannot be picked again', async () => {
        await renderPage({ sheet: sheetWith({ workTaskId: 8 }), taskOptions: [open, done] })
        expect(combo('Task')).toHaveTextContent('Old report (closed)')
        fireEvent.mouseDown(combo('Task'))
        expect(await screen.findByRole('option', { name: /Old report/ })).toHaveAttribute('aria-disabled', 'true')
    })
})
