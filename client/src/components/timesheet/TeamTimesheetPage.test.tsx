import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TimesheetStatusHistory, UserInfo } from '../../lib/types'
import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import TeamTimesheetPage from './TeamTimesheetPage'

/*
 * When an HR Administrator cancels an approval (ReopenTimesheet), the reason is
 * written to the status history and emailed. It also has to be on screen: the
 * sheet lands back in the manager's queue, and a manager asked to review a week
 * they already approved needs to know why without hunting for the email.
 */
vi.mock('../../lib/api')

const api = vi.mocked(await import('../../lib/api'))

const manager: UserInfo = {
    id: 'u-manager',
    userName: 'mark',
    email: 'mark@example.com',
    displayName: 'Mark Manager',
    imageUrl: '',
    roles: ['Manager'],
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
        status: 'Submitted',
        submittedAt: '2026-08-05T06:51:00Z',
        approvedAt: null,
        createdAt: '2026-08-03T00:00:00Z',
        projectSummaries: [],
        dailyHours: [8, 0, 0, 0, 0],
        entries: [],
        ...overrides,
    } as Timesheet
}

function history(overrides: Partial<TimesheetStatusHistory> = {}): TimesheetStatusHistory {
    return {
        id: 'h-1',
        timesheetId: 'ts-1',
        employeeId: 'p-athos',
        employeeName: 'Athos Lamprou',
        changedByUserId: 'u-hr',
        changedByUserName: 'Helen HR',
        oldStatus: 'Approved',
        newStatus: 'Submitted',
        comment: 'Wednesday hours do not match the project log.',
        changedAt: '2026-09-25T08:00:00Z',
        ...overrides,
    }
}

async function renderPage(timesheets: Timesheet[], histories: TimesheetStatusHistory[]) {
    api.getTimesheets.mockResolvedValue(timesheets)
    api.getTimesheetStatusHistories.mockResolvedValue(histories)
    api.getTimesheet.mockImplementation(async (id) => timesheets.find((t) => t.id === id)!)
    api.getEmployeeProfiles.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([])
    api.getProjects.mockResolvedValue([
        { id: 6, code: 'ELLS-001', name: 'Eurobank Lasernet', isActive: true },
        { id: 4, code: 'CJS-001', name: 'CDB jDocs', isActive: true },
    ] as never)
    api.getProjectTypes.mockResolvedValue([])
    api.getProjectComponents.mockResolvedValue([])
    api.getProjectActivityTypes.mockResolvedValue([])
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><TeamTimesheetPage user={manager} /></QueryClientProvider>)
    await screen.findByText(timesheets[0].employeeName)
}

beforeEach(() => vi.clearAllMocks())

describe('Team Timesheets shows the manager why a sheet is back in their queue', () => {
    it('quotes the cancelled approval and its reason on the row', async () => {
        await renderPage([sheet()], [history()])

        expect(await screen.findByText('Approval cancelled by Helen HR')).toBeInTheDocument()
        expect(screen.getByText(/Wednesday hours do not match the project log\./)).toBeInTheDocument()
    })

    it('quotes it again in the View dialog', async () => {
        await renderPage([sheet()], [history()])
        await screen.findByText('Approval cancelled by Helen HR')

        fireEvent.click(screen.getByRole('button', { name: 'View' }))

        const dialog = await screen.findByRole('dialog')
        expect(await within(dialog).findByText('Approval cancelled by Helen HR')).toBeInTheDocument()
        expect(within(dialog).getByText(/Wednesday hours do not match the project log\./)).toBeInTheDocument()
    })

    it('says nothing on a sheet whose latest note no longer applies', async () => {
        // A rejection reason belongs to the Rejected state; once the employee
        // resubmits, the manager is reviewing new work, not the old reason.
        await renderPage(
            [sheet({ status: 'Resubmitted' })],
            [history({ oldStatus: 'Submitted', newStatus: 'Rejected', changedByUserName: 'Mark Manager', comment: 'Old reason' })],
        )

        expect(screen.queryByText(/Old reason/)).not.toBeInTheDocument()
        expect(screen.queryByText(/Rejected by/)).not.toBeInTheDocument()
    })
})

/*
 * The View dialog reads the week as the same five day cards the HR Administrator
 * gets on All Timesheets, each entry filed under the calendar date it carries.
 * Dates are not instants: building the cards from local midnight and reading them
 * back in UTC shifted every entry a day east of Greenwich.
 */
function entry(date: string, projectId: number, hoursWorked: number, notes: string | null = null): TimesheetEntry {
    return {
        id: `e-${date}-${projectId}`,
        timesheetId: 'ts-1',
        projectId,
        date: `${date}T00:00:00`,
        hoursWorked,
        notes,
        projectTypeId: null,
        projectComponentId: null,
    }
}

describe('the View dialog files each entry under its own day', () => {
    it("shows Monday's hours on Monday, by project code", async () => {
        await renderPage(
            [sheet({
                periodStart: '2026-09-21T00:00:00',
                periodEnd: '2026-09-25T00:00:00',
                entries: [
                    entry('2026-09-21', 6, 4),
                    entry('2026-09-21', 4, 4, 'ZX'),
                    entry('2026-09-25', 6, 9),
                    entry('2026-09-25', 4, 4),
                ],
            })],
            [],
        )

        fireEvent.click(screen.getByRole('button', { name: 'View' }))
        const dialog = await screen.findByRole('dialog')

        // The detail loads after the dialog opens; the cards follow it.
        await waitFor(() => expect(dialog.querySelector('[data-day="2026-09-21"]')).not.toBeNull())
        const monday = dialog.querySelector('[data-day="2026-09-21"]') as HTMLElement | null
        // The project catalogue lands a tick after the entries; until it does the
        // card names the project by id, so wait for the code rather than assert it.
        expect(await within(monday!).findByText('ELLS-001')).toBeInTheDocument()
        expect(within(monday!).getByText('8.0h')).toBeInTheDocument()
        expect(within(monday!).getByText('ZX')).toBeInTheDocument()

        const friday = dialog.querySelector('[data-day="2026-09-25"]') as HTMLElement | null
        expect(within(friday!).getByText('13.0h')).toBeInTheDocument()
        expect(within(dialog).queryByText(/Project #/)).not.toBeInTheDocument()
    })
})
