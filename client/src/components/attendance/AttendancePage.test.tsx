import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppSettings, AttendanceHistoryDay, AttendanceToday } from '../../lib/types'
import AttendancePage from './AttendancePage'

/*
 * My Attendance quotes the organisation's break beside today's break time, so an
 * employee can see what the day allows for against what they have taken. The
 * wording comes from `describeBreakPolicy`; with no break configured the line reads
 * as it always did.
 */
// The page reads the history and the settings through the barrel; the today-state
// hook reads its module directly, so both are mocked.
vi.mock('../../lib/api')
vi.mock('../../lib/api/attendance')

const api = vi.mocked(await import('../../lib/api'))
const attendanceApi = vi.mocked(await import('../../lib/api/attendance'))

const TODAY: AttendanceToday = {
    date: '2026-09-24',
    status: 'in',
    checkInAt: '2026-09-24T05:00:00Z',
    checkOutAt: null,
    onBreakSince: null,
    totalBreakMinutes: 25,
    workedMinutes: 180,
    events: [],
    isAutoBreak: false,
}

const SETTINGS: AppSettings = {
    leaveYearStartMonth: 1,
    autoRunRollover: true,
    sendYearEndWarningEmails: true,
    blockLeaveSpanningIntoNextYear: true,
    notifyManagersOfTeamExpiries: true,
    holidayCountryCode: null,
    holidayCountryName: null,
    workingHoursStart: '08:00',
    workingHoursEnd: '17:00',
    timeZoneId: 'UTC',
    financialYearStartMonth: 1,
    workingDays: 'mon-fri',
    workingDaysCustom: 'mon,tue,wed,thu,fri',
    breakMode: 'none',
    breakStart: '13:00',
    breakEnd: '14:00',
    breakMinutes: 0,
    weeklyHoursTarget: 40,
    timesheetSubmissionDeadlineDay: 'fri',
    timesheetSubmissionDeadlineTime: '18:00',
    emailNotificationsEnabled: true,
    emailDailyDigest: true,
    emailUrgentOnly: false,
    reminders: [],
}

beforeEach(() => {
    vi.clearAllMocks()
    attendanceApi.getAttendanceToday.mockResolvedValue(TODAY)
    api.getAttendanceHistory.mockResolvedValue([])
})

async function renderPage(settings: AppSettings) {
    api.getAppSettings.mockResolvedValue(settings)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><AttendancePage /></QueryClientProvider>)
    await screen.findByText(/Total break time/)
}

describe('My Attendance quotes the break the day allows for', () => {
    it('states the allowance beside today\'s break time for a flexible break', async () => {
        await renderPage({ ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByText('Break allowance 1h')).toBeInTheDocument()
        expect(screen.getByText('25 min')).toBeInTheDocument()
    })

    it('states the window for a fixed break', async () => {
        await renderPage({ ...SETTINGS, breakMode: 'fixed', breakStart: '13:00', breakEnd: '13:45' })

        expect(await screen.findByText('Break 13:00–13:45 (45 min)')).toBeInTheDocument()
    })

    it('says nothing about a break the organisation has not set', async () => {
        await renderPage(SETTINGS)

        expect(await screen.findByText('25 min')).toBeInTheDocument()
        expect(screen.queryByText(/Break allowance/)).not.toBeInTheDocument()
        expect(screen.queryByText(/^Break \d/)).not.toBeInTheDocument()
    })
})

/*
 * Once the server compares the break taken with the break allowed
 * (`breakVarianceMinutes`, from WorkingDaySchedule.BreakVariance), the same line
 * says by how much. Null is "nothing to say" and renders nothing, which is also
 * what an API predating the field sends.
 */
describe('My Attendance says how the break compares with the allowance', () => {
    it('says how far over while the day is still open', async () => {
        attendanceApi.getAttendanceToday.mockResolvedValue({ ...TODAY, totalBreakMinutes: 80, breakVarianceMinutes: 20 })
        await renderPage({ ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByText('20 min over')).toBeInTheDocument()
    })

    it('says how far under once checked out', async () => {
        attendanceApi.getAttendanceToday.mockResolvedValue({
            ...TODAY, status: 'done', checkOutAt: '2026-09-24T14:00:00Z', totalBreakMinutes: 30, breakVarianceMinutes: -30,
        })
        await renderPage({ ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByText('30 min under')).toBeInTheDocument()
    })

    it('says nothing when the server has nothing to say', async () => {
        attendanceApi.getAttendanceToday.mockResolvedValue({ ...TODAY, breakVarianceMinutes: null })
        await renderPage({ ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByText('Break allowance 1h')).toBeInTheDocument()
        expect(screen.queryByText(/min over|min under/)).not.toBeInTheDocument()
    })

    it('shows the variance beside the break in the history table', async () => {
        api.getAttendanceHistory.mockResolvedValue([
            {
                date: '2026-09-23', status: 'complete', checkInAt: '2026-09-23T05:00:00Z', checkOutAt: '2026-09-23T14:00:00Z',
                totalBreakMinutes: 90, workedMinutes: 450, breakVarianceMinutes: 30,
            },
            {
                date: '2026-09-22', status: 'complete', checkInAt: '2026-09-22T05:00:00Z', checkOutAt: '2026-09-22T14:00:00Z',
                totalBreakMinutes: 45, workedMinutes: 495, breakVarianceMinutes: -15,
            },
        ])
        await renderPage({ ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByText('30 min over')).toBeInTheDocument()
        expect(screen.getByText('15 min under')).toBeInTheDocument()
    })
})

function historyDay(overrides: Partial<AttendanceHistoryDay>): AttendanceHistoryDay {
    return {
        date: '2026-09-22', status: 'complete', checkInAt: '2026-09-22T08:00:00Z', checkOutAt: '2026-09-22T17:00:00Z',
        totalBreakMinutes: 60, workedMinutes: 480, breakVarianceMinutes: null,
        ...overrides,
    }
}

describe('My Attendance flags a short day and reads leave as leave', () => {
    it('grades a short day with how far short it was', async () => {
        api.getAttendanceHistory.mockResolvedValue([
            historyDay({ status: 'short', workedMinutes: 390, shortByMinutes: 90, targetMinutes: 480 }),
        ])
        await renderPage(SETTINGS)

        expect(await screen.findByText('1h 30m short')).toBeInTheDocument()
    })

    it('reads a leave day as leave and a weekend as a day off', async () => {
        api.getAttendanceHistory.mockResolvedValue([
            historyDay({ date: '2026-09-19', status: 'off', checkInAt: null, checkOutAt: null, workedMinutes: 0, totalBreakMinutes: 0 }),
            historyDay({ date: '2026-09-21', status: 'leave', onLeave: true, checkInAt: null, checkOutAt: null, workedMinutes: 0, totalBreakMinutes: 0 }),
        ])
        await renderPage(SETTINGS)

        expect(await screen.findByText('On leave')).toBeInTheDocument()
        expect(screen.getByText('Day off')).toBeInTheDocument()
        expect(screen.queryByText('No record')).not.toBeInTheDocument()
    })

    it('reads an older API with no short-day fields as it always did', async () => {
        api.getAttendanceHistory.mockResolvedValue([historyDay({ status: 'complete' })])
        await renderPage(SETTINGS)

        expect(await screen.findByText('Complete')).toBeInTheDocument()
        expect(screen.queryByText(/short$/)).not.toBeInTheDocument()
    })
})
