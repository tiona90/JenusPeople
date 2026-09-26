import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamAttendance, TeamMemberAttendance, TeamWeekRow } from '../../lib/types'
import TeamAttendancePage from './TeamAttendancePage'

/*
 * The team board tells a Manager or an HR Administrator how each person's break
 * compares with the break the Organization settings allow for. The comparison is
 * the server's (`breakVarianceMinutes`, WorkingDaySchedule.BreakVariance); the
 * board only words it. Null is "nothing to say": no break configured, or a day
 * still open that has not gone over — and what an API predating the field sends.
 */
vi.mock('../../lib/api')

const api = vi.mocked(await import('../../lib/api'))

function member(overrides: Partial<TeamMemberAttendance>): TeamMemberAttendance {
    return {
        employeeId: 'p-1',
        employeeName: 'Someone',
        departmentName: 'Engineering',
        jobTitle: null,
        status: 'in',
        checkInAt: '2026-09-24T05:00:00Z',
        workedMinutes: 240,
        onBreakSince: null,
        todayNote: 'On track',
        isAutoBreak: false,
        breakMinutes: 0,
        breakVarianceMinutes: null,
        ...overrides,
    }
}

async function renderBoard(members: TeamMemberAttendance[]) {
    const data: TeamAttendance = { members, week: [] }
    api.getTeamAttendance.mockResolvedValue(data)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><TeamAttendancePage /></QueryClientProvider>)
    await screen.findByText(members[0].employeeName)
}

beforeEach(() => vi.clearAllMocks())

describe('Team board compares each break with the allowance', () => {
    it('says how far over while the person is still working', async () => {
        await renderBoard([member({ employeeId: 'p-owen', employeeName: 'Owen Over', breakMinutes: 80, breakVarianceMinutes: 20 })])

        expect(screen.getByText('Break 1h 20m')).toBeInTheDocument()
        expect(screen.getByText('20 min over')).toBeInTheDocument()
    })

    it('says how far under once the person has checked out', async () => {
        await renderBoard([member({
            employeeId: 'p-una', employeeName: 'Una Under', status: 'out', todayNote: 'Done at 14:00',
            breakMinutes: 30, breakVarianceMinutes: -30,
        })])

        expect(screen.getByText('Break 30 min')).toBeInTheDocument()
        expect(screen.getByText('30 min under')).toBeInTheDocument()
    })

    it('quotes the break taken with no verdict when there is nothing to say', async () => {
        await renderBoard([member({ employeeId: 'p-ivy', employeeName: 'Ivy Inside', breakMinutes: 10, breakVarianceMinutes: null })])

        expect(screen.getByText('Break 10 min')).toBeInTheDocument()
        expect(screen.queryByText(/min over|min under/)).not.toBeInTheDocument()
    })

    it('says nothing about a break nobody has taken yet', async () => {
        await renderBoard([member({ employeeId: 'p-new', employeeName: 'Nia New', breakMinutes: 0 })])

        expect(screen.queryByText(/^Break /)).not.toBeInTheDocument()
    })

    it('reads an older API with no break fields as nothing to say', async () => {
        const legacy = member({ employeeId: 'p-old', employeeName: 'Old Payload' })
        delete (legacy as Partial<TeamMemberAttendance>).breakMinutes
        delete (legacy as Partial<TeamMemberAttendance>).breakVarianceMinutes
        await renderBoard([legacy])

        expect(screen.queryByText(/^Break /)).not.toBeInTheDocument()
        expect(screen.queryByText(/min over|min under/)).not.toBeInTheDocument()
    })
})

async function renderWithWeek(week: TeamWeekRow[], extra: Partial<TeamAttendance> = {}) {
    api.getTeamAttendance.mockResolvedValue({ members: [member({ employeeName: 'Someone' })], week, ...extra })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><TeamAttendancePage /></QueryClientProvider>)
    await screen.findByText('Weekly Attendance Log')
}

const days = (overrides: Partial<TeamWeekRow['days'][number]>[]): TeamWeekRow['days'] =>
    overrides.map((o, i) => ({ date: `2026-09-2${1 + i}`, workedMinutes: 480, note: null, ...o }))

describe('Team board marks short days and leave on the week grid', () => {
    it('says how far short a day was, and reads a missing day as no attendance', async () => {
        await renderWithWeek([{
            employeeId: 'p-sam', employeeName: 'Sam Short', totalMinutes: 870,
            days: days([{}, { workedMinutes: 390, shortByMinutes: 90 }, { workedMinutes: null, shortByMinutes: 480 }, { workedMinutes: null }, { workedMinutes: null }]),
        }])

        expect(screen.getByText('1h 30m short')).toBeInTheDocument()
        expect(screen.getByText('No attendance')).toBeInTheDocument()
    })

    it('reads a leave day as leave instead of a dash', async () => {
        await renderWithWeek([{
            employeeId: 'p-lea', employeeName: 'Lea Leave', totalMinutes: 0,
            days: days([{ workedMinutes: null, onLeave: true }, { workedMinutes: null }, { workedMinutes: null }, { workedMinutes: null }, { workedMinutes: null }]),
        }])

        expect(screen.getByText('Leave')).toBeInTheDocument()
    })

    it("names the previous working day's short days above the grid", async () => {
        await renderWithWeek(
            [{ employeeId: 'p-sam', employeeName: 'Sam Short', totalMinutes: 0, days: days([{}, {}, {}, {}, {}]) }],
            {
                shortDaysDate: '2026-09-22',
                shortDays: [{ employeeId: 'p-sam', employeeName: 'Sam Short', departmentName: 'Engineering', workedMinutes: 390, shortByMinutes: 90 }],
            },
        )

        expect(screen.getByText(/Short days on Tue 22 Sep/)).toBeInTheDocument()
        expect(screen.getByText(/Sam Short · 1h 30m short/)).toBeInTheDocument()
    })

    it('says nothing when nobody was short', async () => {
        await renderWithWeek([{ employeeId: 'p-sam', employeeName: 'Sam Short', totalMinutes: 0, days: days([{}, {}, {}, {}, {}]) }])

        expect(screen.queryByText(/Short days on/)).not.toBeInTheDocument()
    })
})
