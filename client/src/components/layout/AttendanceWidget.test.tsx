import { act, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppSettings, AttendanceToday } from '../../lib/types'
import AttendanceWidget from './AttendanceWidget'

/*
 * While somebody is on a break the topbar pill runs a clock: what is left of the
 * day's break allowance counting down, then the time past it counting up, and the
 * running break counting up where no allowance is configured.
 */
vi.mock('../../lib/api')
vi.mock('../../lib/api/attendance')

const api = vi.mocked(await import('../../lib/api'))
const attendanceApi = vi.mocked(await import('../../lib/api/attendance'))

const NOW = new Date('2026-09-25T12:10:00Z')

const ON_BREAK: AttendanceToday = {
    date: '2026-09-25',
    status: 'break',
    checkInAt: '2026-09-25T06:00:00Z',
    checkOutAt: null,
    onBreakSince: '2026-09-25T12:00:00Z',
    totalBreakMinutes: 0,
    workedMinutes: 360,
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
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
    vi.setSystemTime(NOW)
})

afterEach(() => {
    vi.useRealTimers()
})

function renderWidget(today: AttendanceToday, settings: AppSettings) {
    attendanceApi.getAttendanceToday.mockResolvedValue(today)
    api.getAppSettings.mockResolvedValue(settings)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><AttendanceWidget enabled /></QueryClientProvider>)
}

describe('the break clock on the topbar pill', () => {
    it('counts the allowance down a second at a time', async () => {
        renderWidget(ON_BREAK, { ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByRole('timer', { name: '50:00 of break left' })).toBeInTheDocument()
        act(() => { vi.advanceTimersByTime(1000) })
        expect(screen.getByRole('timer', { name: '49:59 of break left' })).toBeInTheDocument()
    })

    it('counts the breaks already taken today against the allowance', async () => {
        renderWidget({ ...ON_BREAK, totalBreakMinutes: 30 }, { ...SETTINGS, breakMode: 'fixed' })

        expect(await screen.findByRole('timer', { name: '20:00 of break left' })).toBeInTheDocument()
    })

    it('counts up the time past the allowance', async () => {
        renderWidget({ ...ON_BREAK, totalBreakMinutes: 55 }, { ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByRole('timer', { name: '05:00 over break allowance' })).toBeInTheDocument()
        expect(screen.getByText('+05:00')).toBeInTheDocument()
    })

    it('counts the running break up when no allowance is configured', async () => {
        renderWidget(ON_BREAK, SETTINGS)

        expect(await screen.findByRole('timer', { name: 'On break for 10:00' })).toBeInTheDocument()
    })

    it('shows no clock while working', async () => {
        renderWidget({ ...ON_BREAK, status: 'in', onBreakSince: null }, { ...SETTINGS, breakMode: 'flexible', breakMinutes: 60 })

        expect(await screen.findByText(/In since/)).toBeInTheDocument()
        expect(screen.queryByRole('timer')).not.toBeInTheDocument()
    })
})
