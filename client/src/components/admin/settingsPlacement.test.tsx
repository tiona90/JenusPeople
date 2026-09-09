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
    updateLeaveType: vi.fn(),
    resetReminders: vi.fn(),
    clearApprovalHistory: vi.fn(),
}))

const api = vi.mocked(await import('../../lib/api'))

const SETTINGS: AppSettings = {
    leaveYearStartMonth: 1,
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

/* The row both figures live in: the allowance and the cap that bounds it. Leave Types
   is the only screen that edits either. */
const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', requiresApproval: true, isActive: true, affectsBalance: true,
    icon: '🌴', colorKey: 'annual', description: 'Vacation days.', paid: true,
    attachmentPolicy: 'None', defaultAllowance: 25, allowanceUnit: 'days/year',
    maxCarryoverDays: 5,
    accrualNotes: 'Resets 1 Jan', minNoticeDays: 7, maxConsecutiveDays: 15,
    halfDayAllowed: true, eligibilityNotes: 'All employees', eligibilityScope: 'All',
} as const

beforeEach(() => {
    vi.clearAllMocks()
    api.getAppSettings.mockResolvedValue(SETTINGS)
    api.getDepartments.mockResolvedValue([])
    api.getEmployeeProfiles.mockResolvedValue([])
    api.getHolidayCountries.mockResolvedValue([])
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE] as never)
    api.updateAppSettings.mockResolvedValue(SETTINGS)
    api.updateLeaveType.mockResolvedValue(ANNUAL_LEAVE_TYPE as never)
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

/*
 * Two numbers describe an annual-leave budget: how many days it grants, and how many
 * unused ones survive the year end. Both used to be editable here — the allowance as a
 * second surface onto LeaveType.DefaultAllowance, the cap as an org-wide AppSettings
 * column sitting a screen away from the allowance it bounds, unable to say that sick
 * leave carries nothing while annual leave carries five.
 *
 * Both are columns on the leave type now, and Leave Types is the only screen that
 * writes either. Leave Settings still quotes them, because the carryover preview is
 * meaningless without them, but it cannot edit them and no longer saves a leave type.
 */
describe('the allowance and its carryover cap are edited only on Leave Types', () => {
    it('offers neither as an input', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.queryByText('Max Carryover Days')).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/annual leave allowance/i)).not.toBeInTheDocument()
        // The older duplicates this page has already shed, still gone.
        expect(screen.queryByText('Default for New Employees (days)')).not.toBeInTheDocument()
        expect(screen.queryByText(/Fallback Entitlement/)).not.toBeInTheDocument()
    })

    it('saves app settings without touching a leave type, and sends neither figure', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // Save is gated on isDirty, so move something this page does still own.
        const weeklyHours = screen.getByText('Weekly Hours Target')
            .parentElement!.querySelector('input')!
        fireEvent.change(weeklyHours, { target: { value: '35' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }))

        await waitFor(() => expect(api.updateAppSettings).toHaveBeenCalledTimes(1))
        expect(api.updateLeaveType).not.toHaveBeenCalled()
        const sent = api.updateAppSettings.mock.calls[0][0]
        expect(sent).not.toHaveProperty('maxCarryoverDays')
        expect(sent).not.toHaveProperty('defaultAnnualEntitlement')
    })

    /* The leave-year card quoted both figures in a banner of its own once it stopped
       editing them. That banner is gone too: a form restating a setting it cannot
       change is just a second place to read a stale number. Both figures survive where
       they are being applied — the carryover preview and the Carryover Cap tile. */
    it('does not restate either figure in the leave-year card', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.queryByText(/Both are set per leave type, on Leave Types/)).not.toBeInTheDocument()
        expect(screen.queryByText(/Annual leave allows/)).not.toBeInTheDocument()
    })

    it('quotes both figures where they are applied', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByText(/entitlement 25 days\/year — both from Leave Types/)).toBeInTheDocument()
        // The sidebar's Carryover Cap tile, and the rollover line beside it.
        expect(screen.getByText('5 days')).toBeInTheDocument()
        expect(screen.getByText(/auto-calculate carryover \(max 5 days\)/)).toBeInTheDocument()
    })

    it('follows the leave type when the cap there changes', async () => {
        api.getLeaveTypes.mockResolvedValue([{ ...ANNUAL_LEAVE_TYPE, maxCarryoverDays: 12 }] as never)
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // The preview scenarios are computed from the cap, not from app settings.
        expect(screen.getByText('At cap (= 12 days unused)')).toBeInTheDocument()
        expect(screen.getAllByText('12 days').length).toBeGreaterThan(0)
    })

    /* With no annual-leave type there is nothing to quote. The page reads as zero
       rather than falling back to a figure of its own — that fallback was the drift. */
    it('reads as nothing configured when there is no annual-leave type', async () => {
        api.getLeaveTypes.mockResolvedValue([])
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByText(/entitlement 0 days\/year/)).toBeInTheDocument()
        expect(screen.getByText('At cap (= 0 days unused)')).toBeInTheDocument()
    })
})

/*
 * The year-end and final warning days were two more inputs on this page, stored as
 * AppSettings columns that nothing read but the schedule preview beside them. No job
 * sends a warning email on that lead time, so changing 30 to 45 changed a caption and
 * a preview date and nothing an employee would ever see. The lead times are stated by
 * the page now; the columns are gone (migration RemoveAppSettingsWarningDays).
 */
describe('the year-end warning lead times are not settings', () => {
    it('offers neither as an input', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.queryByText('Year-End Warning (days before)')).not.toBeInTheDocument()
        expect(screen.queryByText('Final Warning (days before)')).not.toBeInTheDocument()
    })

    it('still shows both warnings in the schedule, and saves neither figure', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Upcoming Schedule')

        expect(screen.getByText('30-day warning emails')).toBeInTheDocument()
        expect(screen.getByText('7-day final warning')).toBeInTheDocument()

        const weeklyHours = screen.getByText('Weekly Hours Target')
            .parentElement!.querySelector('input')!
        fireEvent.change(weeklyHours, { target: { value: '35' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }))

        await waitFor(() => expect(api.updateAppSettings).toHaveBeenCalledTimes(1))
        const sent = api.updateAppSettings.mock.calls[0][0]
        expect(sent).not.toHaveProperty('yearEndWarningDays')
        expect(sent).not.toHaveProperty('finalWarningDays')
    })
})

describe('the reminders page reads settings that were already cached', () => {
    it('shows the cached data instead of "Failed to load settings" when another page warmed the query cache first', async () => {
        // Dashboard, AppSettingsPanel and others all query ['appSettings'], so by the
        // time an admin reaches this page the data is typically already cached — the
        // query resolves synchronously on mount with isLoading/isError both false.
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        queryClient.setQueryData(['appSettings'], SETTINGS)
        render(<QueryClientProvider client={queryClient}><OrgSettingsPanel /></QueryClientProvider>)

        await screen.findByText('🔔 Notification Settings')
        expect(screen.queryByText('Failed to load settings.')).not.toBeInTheDocument()
        expect(screen.getByText('Pending Approvals')).toBeInTheDocument()
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
