import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppSettings } from '../../lib/types'
import AppSettingsPanel from './AppSettingsPanel'
import DataMaintenancePanel from './DataMaintenancePanel'
import OrgSettingsPanel from './OrgSettingsPanel'

/*
 * Three unrelated things shared the "Notification Settings" page: the reminder
 * schedule, an Organization Settings block, and a Danger Zone whose button deletes
 * thirty days of approval records for good. The organization block also duplicated
 * the leave year — its "Financial year · When leave allocations reset" wrote
 * FinancialYearStartMonth while Leave Settings wrote LeaveYearStartMonth, and an
 * admin could set the two to different months with nothing to warn them.
 *
 * Each setting now has one home, and the destructive action is not on a page of
 * preferences.
 */
vi.mock('../../lib/api', () => ({
    getAppSettings: vi.fn(),
    getDepartments: vi.fn(),
    getEmployeeProfiles: vi.fn(),
    getHolidayCountries: vi.fn(),
    getLeaveTypes: vi.fn(),
    updateAppSettings: vi.fn(),
    resetReminders: vi.fn(),
    clearApprovalHistory: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

const SETTINGS: AppSettings = {
    leaveYearStartMonth: 1,
    maxCarryoverDays: 5,
    defaultAnnualEntitlement: 20,
    yearEndWarningDays: 30,
    finalWarningDays: 7,
    autoRunRollover: true,
    sendYearEndWarningEmails: true,
    blockLeaveSpanningIntoNextYear: true,
    notifyManagersOfTeamExpiries: true,
    holidayCountryCode: 'CY',
    holidayCountryName: 'Cyprus',
    workingHoursStart: '09:00',
    workingHoursEnd: '18:00',
    timeZoneId: 'UTC',
    // Deliberately out of step with leaveYearStartMonth: this is the drift the
    // duplicate control allowed.
    financialYearStartMonth: 7,
    workingDays: 'mon-fri',
    workingDaysCustom: 'mon,tue,wed,thu,fri',
    weeklyHoursTarget: 40,
    timesheetSubmissionDeadlineDay: 'fri',
    timesheetSubmissionDeadlineTime: '18:00',
    emailNotificationsEnabled: true,
    emailDailyDigest: true,
    emailUrgentOnly: false,
    slackEnabled: false,
    slackConnected: false,
    reminders: [
        { id: 'pending-approvals', enabled: true, time: '09:00', frequency: 'daily' },
        { id: 'low-balance', enabled: false, time: '10:00', frequency: 'weekly' },
    ],
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getAppSettings.mockResolvedValue(SETTINGS)
    api.getDepartments.mockResolvedValue([])
    api.getEmployeeProfiles.mockResolvedValue([])
    api.getHolidayCountries.mockResolvedValue([])
    api.getLeaveTypes.mockResolvedValue([])
    api.updateAppSettings.mockResolvedValue(SETTINGS)
})

function renderPanel(ui: React.ReactElement) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

/** Every select on screen, by the option labels it offers. */
function selectOptionSets() {
    return screen.queryAllByRole('combobox').map((el) => el.textContent ?? '')
}

describe('the leave year is editable in exactly one place', () => {
    it('offers the year on Organization settings', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByText('Leave Year Start Month')).toBeInTheDocument()
        expect(screen.getByText('Also the financial year — when leave allocations reset')).toBeInTheDocument()
    })

    it('offers no year — financial or leave — on Notification Settings', async () => {
        renderPanel(<OrgSettingsPanel />)
        await screen.findByText('🔔 Notification Settings')

        expect(screen.queryByText('Financial year')).not.toBeInTheDocument()
        expect(screen.queryByText('When leave allocations reset')).not.toBeInTheDocument()
        // The month labels the duplicate control offered are gone with it.
        expect(selectOptionSets().join(' ')).not.toContain('January 1 – December 31')
    })

    it('writes the financial-year column from the leave year, so the two cannot drift', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // The stored settings start out inconsistent (leave year January, financial
        // year July). Moving the leave year to April sends April for both.
        const monthSelect = screen.getByText('Leave Year Start Month')
            .parentElement!.querySelector('[role="combobox"]')!
        fireEvent.mouseDown(monthSelect)
        fireEvent.click(await screen.findByRole('option', { name: 'April' }))
        fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }))

        await waitFor(() => expect(api.updateAppSettings).toHaveBeenCalledTimes(1))
        const sent = api.updateAppSettings.mock.calls[0][0]
        expect(sent.leaveYearStartMonth).toBe(4)
        expect(sent.financialYearStartMonth).toBe(4)
    })
})

describe('each settings block has one home', () => {
    it('keeps the organization block with the rest of the org-wide settings', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // Moved here, alongside the timesheet policy and the public-holiday country
        // that were already on this page.
        expect(screen.getByText('Working hours start')).toBeInTheDocument()
        expect(screen.getByText('Timezone')).toBeInTheDocument()
        expect(screen.getByText('Weekends')).toBeInTheDocument()
        expect(screen.getByText('Timesheet Policy')).toBeInTheDocument()
    })

    it('leaves the reminders page to reminders and notifications', async () => {
        renderPanel(<OrgSettingsPanel />)
        await screen.findByText('🔔 Notification Settings')

        expect(screen.getByText('Reminders')).toBeInTheDocument()
        expect(screen.getByText('Email Notifications')).toBeInTheDocument()
        expect(screen.queryByText('Organization Settings')).not.toBeInTheDocument()
        expect(screen.queryByText('Working hours start')).not.toBeInTheDocument()

        // Removing the organization card took the page's only Save button with it.
        expect(screen.getByRole('button', { name: /Save changes/ })).toBeInTheDocument()
    })
})

describe('the irreversible action is off the preferences page', () => {
    it('no longer sits under the notification toggles', async () => {
        renderPanel(<OrgSettingsPanel />)
        await screen.findByText('🔔 Notification Settings')

        expect(screen.queryByText('Danger Zone')).not.toBeInTheDocument()
        expect(screen.queryByText('Clear all approval history')).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /Clear history/ })).not.toBeInTheDocument()

        // Resetting reminders stays: it is a preference reset, on the page whose
        // preferences it resets.
        expect(screen.getByRole('button', { name: /Reset reminders to defaults/ })).toBeInTheDocument()
    })

    it('lives on Data Maintenance, which holds nothing else', () => {
        renderPanel(<DataMaintenancePanel />)

        expect(screen.getByText('Clear all approval history')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /Clear history/ })).toBeInTheDocument()
        expect(screen.getByText(/This cannot be undone/)).toBeInTheDocument()
        // Nothing on this page is a preference.
        expect(screen.queryAllByRole('checkbox')).toHaveLength(0)
    })
})
