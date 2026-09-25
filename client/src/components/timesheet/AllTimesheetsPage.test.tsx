import { fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UserInfo } from '../../lib/types'
import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import AllTimesheetsPage from './AllTimesheetsPage'

/*
 * An HR Administrator's All Timesheets leaves Drafts out: an employee's unsubmitted
 * work is nobody's to review yet. The status filter has to agree with the list it
 * filters — offering "Draft" there selects rows the page never shows, so the choice
 * empties the page and says nothing about why.
 */
vi.mock('../../lib/api')
vi.mock('../../lib/mobx', () => ({ useStore: vi.fn() }))
// The hook reads the settings API directly, not through lib/api, so it is mocked
// on its own; the page falls back to its defaults when no settings have loaded.
vi.mock('../../lib/hooks/useAppSettings', () => ({ useAppSettings: () => ({ data: undefined }) }))

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const hrAdministrator: UserInfo = {
    id: 'u-hr',
    userName: 'helen',
    email: 'helen@example.com',
    displayName: 'Helen HR',
    imageUrl: '',
    roles: ['HR Administrator'],
}

const systemAdministrator: UserInfo = {
    ...hrAdministrator,
    id: 'u-sys',
    userName: 'sam',
    email: 'sam@example.com',
    displayName: 'Sam Sysadmin',
    roles: ['System Administrator'],
}

function sheet(overrides: Partial<Timesheet> = {}): Timesheet {
    return {
        id: 'ts-1',
        employeeId: 'p-athos',
        employeeName: 'Athos Lamprou',
        departmentId: 1,
        periodStart: '2026-08-03T00:00:00Z',
        periodEnd: '2026-08-07T00:00:00Z',
        totalHours: 8,
        status: 'Approved',
        submittedAt: '2026-08-05T06:51:00Z',
        approvedAt: '2026-08-06T06:51:00Z',
        createdAt: '2026-08-03T00:00:00Z',
        projectSummaries: [],
        dailyHours: [8, 0, 0, 0, 0],
        entries: [],
        awaitingManager: false,
        ...overrides,
    } as Timesheet
}

const approved = sheet()
const draft = sheet({
    id: 'ts-2',
    employeeName: 'Dora Drafter',
    status: 'Draft',
    submittedAt: null,
    approvedAt: null,
    periodStart: '2026-07-27T00:00:00Z',
    periodEnd: '2026-07-31T00:00:00Z',
})

async function renderPage(user: UserInfo, timesheets: Timesheet[] = [approved, draft]) {
    mobx.useStore.mockReturnValue({ authStore: { user } } as never)
    api.getTimesheets.mockResolvedValue(timesheets)
    api.getTimesheet.mockImplementation(async (id) => timesheets.find((t) => t.id === id)!)
    api.getEmployeeProfiles.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([])
    api.getProjects.mockResolvedValue([
        { id: 6, code: 'ELLS-001', name: 'Eurobank Lasernet' },
        { id: 4, code: 'CJS-001', name: 'CDB jDocs' },
    ] as never)
    api.getProjectTypes.mockResolvedValue([])
    api.getProjectComponents.mockResolvedValue([])
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><AllTimesheetsPage /></QueryClientProvider>)
    await screen.findByText(timesheets[0].employeeName)
}

function openStatusFilter() {
    const trigger = screen.getAllByRole('combobox').find((el) => el.textContent?.includes('All statuses'))
    expect(trigger).toBeDefined()
    fireEvent.mouseDown(trigger!)
    return screen.getByRole('listbox')
}

beforeEach(() => vi.clearAllMocks())

describe('the status filter on All Timesheets offers only what the list can show', () => {
    it('has no Draft option for an HR Administrator', async () => {
        await renderPage(hrAdministrator)
        expect(screen.queryByText(draft.employeeName)).not.toBeInTheDocument()

        const listbox = openStatusFilter()

        expect(within(listbox).getByRole('option', { name: 'Approved' })).toBeInTheDocument()
        expect(within(listbox).queryByRole('option', { name: 'Draft' })).not.toBeInTheDocument()
    })

    it('keeps the Draft option for a System Administrator, who sees every row', async () => {
        await renderPage(systemAdministrator)
        expect(screen.getByText(draft.employeeName)).toBeInTheDocument()

        const listbox = openStatusFilter()

        expect(within(listbox).getByRole('option', { name: 'Draft' })).toBeInTheDocument()
    })
})

/*
 * The daily breakdown keys each card by a calendar date and matches entries on
 * theirs. Both are dates, not instants: a Monday entry is Monday in Nicosia and in
 * London alike. Building the key from local midnight and reading it back in UTC
 * put every card one day behind east of Greenwich — Monday read "Nothing logged"
 * while its rows sat under Tue, and Friday's never showed at all.
 */
function entry(date: string, projectId: number, hoursWorked: number, notes: string | null = null): TimesheetEntry {
    return {
        id: `e-${date}-${projectId}`,
        timesheetId: 'ts-sept',
        projectId,
        date: `${date}T00:00:00`,
        hoursWorked,
        notes,
        projectTypeId: null,
        projectComponentId: null,
    }
}

const septemberWeek = sheet({
    id: 'ts-sept',
    employeeName: 'Theodoros Iona',
    status: 'Rejected',
    periodStart: '2026-09-21T00:00:00',
    periodEnd: '2026-09-25T00:00:00',
    totalHours: 46,
    entries: [
        entry('2026-09-21', 6, 4),
        entry('2026-09-21', 4, 4, 'ZX'),
        entry('2026-09-22', 4, 4),
        entry('2026-09-22', 6, 4),
        entry('2026-09-23', 6, 5),
        entry('2026-09-23', 4, 4),
        entry('2026-09-24', 6, 1),
        entry('2026-09-24', 4, 7),
        entry('2026-09-25', 6, 9),
        entry('2026-09-25', 4, 4),
    ],
})

async function expandBreakdown() {
    await renderPage(systemAdministrator, [septemberWeek])
    fireEvent.click(screen.getByText(septemberWeek.employeeName))
    await screen.findByText('Daily breakdown')
}

function dayCard(date: string) {
    const card = document.querySelector(`[data-day="${date}"]`)
    expect(card, `card for ${date}`).not.toBeNull()
    return within(card as HTMLElement)
}

describe('the daily breakdown files each entry under its own date', () => {
    it("puts Monday's entries on Monday, not Tuesday", async () => {
        await expandBreakdown()

        const monday = dayCard('2026-09-21')
        expect(monday.getByText('Mon')).toBeInTheDocument()
        expect(monday.getByText('8.0h')).toBeInTheDocument()
        expect(monday.getByText('ZX')).toBeInTheDocument()
        expect(monday.queryByText('Nothing logged')).not.toBeInTheDocument()
    })

    it("shows Friday's entries instead of dropping them", async () => {
        await expandBreakdown()

        expect(dayCard('2026-09-25').getByText('13.0h')).toBeInTheDocument()
    })

    it('names the project by its code rather than its id', async () => {
        await expandBreakdown()

        const monday = dayCard('2026-09-21')
        expect(monday.getByText('ELLS-001')).toBeInTheDocument()
        expect(monday.getByText('CJS-001')).toBeInTheDocument()
        expect(screen.queryByText(/Project #/)).not.toBeInTheDocument()
    })
})
