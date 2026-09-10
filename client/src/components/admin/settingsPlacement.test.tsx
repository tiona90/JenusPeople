import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
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
    perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0,
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

        // Save is gated on the card being dirty, so move something this page does
        // still own. Weekly hours sits on the Organization card, which saves its own.
        fireEvent.change(screen.getByLabelText('Weekly hours target'), { target: { value: '35' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

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

        expect(screen.getByText(/entitlement of 25 days\/year — both set on Leave Types/)).toBeInTheDocument()
        // The sidebar's Carryover Cap tile, and the rollover line beside it.
        expect(screen.getByText('5 days')).toBeInTheDocument()
        expect(screen.getByText(/auto-calculate carryover \(max 5 days\)/)).toBeInTheDocument()
    })

    it('follows the leave type when the cap there changes', async () => {
        api.getLeaveTypes.mockResolvedValue([{ ...ANNUAL_LEAVE_TYPE, maxCarryoverDays: 12 }] as never)
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // The preview is computed from the cap on the leave type, not from app settings.
        expect(screen.getByText(/12-day carryover cap/)).toBeInTheDocument()
        expect(screen.getAllByText('12 days').length).toBeGreaterThan(0)
    })

    /* With no annual-leave type there is nothing to quote. The page reads as zero
       rather than falling back to a figure of its own — that fallback was the drift. */
    it('reads as nothing configured when there is no annual-leave type', async () => {
        api.getLeaveTypes.mockResolvedValue([])
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByText(/entitlement of 0 days\/year/)).toBeInTheDocument()
        expect(screen.getByText(/0-day carryover cap/)).toBeInTheDocument()
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

        fireEvent.change(screen.getByLabelText('Weekly hours target'), { target: { value: '35' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

        await waitFor(() => expect(api.updateAppSettings).toHaveBeenCalledTimes(1))
        const sent = api.updateAppSettings.mock.calls[0][0]
        expect(sent).not.toHaveProperty('yearEndWarningDays')
        expect(sent).not.toHaveProperty('finalWarningDays')
    })
})

/*
 * Both cards on this page edit one settings object, and both Save buttons used to post
 * the whole of it — so saving the leave year also committed whatever the admin had
 * typed into the Organization card and not yet saved, and vice versa. Each card now
 * builds its payload from the last saved settings and overrides only its own fields.
 */
describe('each card saves only its own fields', () => {
    it('saving the leave year leaves the organization card\'s unsaved edits out of the payload', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // An edit left sitting in the other card.
        fireEvent.change(screen.getByLabelText('Weekly hours target'), { target: { value: '35' } })

        const monthSelect = screen.getByText('Leave Year Start Month')
            .parentElement!.querySelector('[role="combobox"]')!
        fireEvent.mouseDown(monthSelect)
        fireEvent.click(await screen.findByRole('option', { name: 'April' }))
        fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }))

        await waitFor(() => expect(api.updateAppSettings).toHaveBeenCalledTimes(1))
        const sent = api.updateAppSettings.mock.calls[0][0]
        expect(sent.leaveYearStartMonth).toBe(4)
        expect(sent.weeklyHoursTarget).toBe(40) // the saved value, not the typed 35
    })

    it('saving the organization card leaves the leave year\'s unsaved edits out of the payload', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        const monthSelect = screen.getByText('Leave Year Start Month')
            .parentElement!.querySelector('[role="combobox"]')!
        fireEvent.mouseDown(monthSelect)
        fireEvent.click(await screen.findByRole('option', { name: 'April' }))

        fireEvent.change(screen.getByLabelText('Weekly hours target'), { target: { value: '35' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

        await waitFor(() => expect(api.updateAppSettings).toHaveBeenCalledTimes(1))
        const sent = api.updateAppSettings.mock.calls[0][0]
        expect(sent.weeklyHoursTarget).toBe(35)
        expect(sent.leaveYearStartMonth).toBe(1) // the saved month, not the chosen April
        // Still mirrored onto the column nothing edits, so the two cannot drift.
        expect(sent.financialYearStartMonth).toBe(1)
    })

    it('gates each Save on its own card being dirty', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByRole('button', { name: 'Save Settings' })).toBeDisabled()
        expect(screen.getByRole('button', { name: 'Save Changes' })).toBeDisabled()

        // An organization edit must not arm the leave-year card's Save.
        fireEvent.change(screen.getByLabelText('Weekly hours target'), { target: { value: '35' } })
        expect(screen.getByRole('button', { name: 'Save Changes' })).toBeEnabled()
        expect(screen.getByRole('button', { name: 'Save Settings' })).toBeDisabled()
    })
})

/*
 * Both year-end warning switches are email preferences, and they now sit with the other
 * email preferences rather than on the leave-year form — an admin looking for which
 * emails go out was reading a page about when the leave year turns over.
 *
 * Leave Settings still *reports* whether the emails are on, in the Upcoming Schedule
 * card that lists them, because a card claiming "Scheduled" for an email nobody sends
 * is worse than not mentioning it. It reports; it does not edit.
 */
describe('the year-end warning emails are configured with the other email preferences', () => {
    it('offers both switches on Notification Settings', async () => {
        renderPanel(<OrgSettingsPanel />)
        await screen.findByText('🔔 Notification Settings')

        expect(screen.getByRole('switch', { name: 'Send year-end warning emails' })).toBeInTheDocument()
        expect(screen.getByRole('switch', { name: 'Also notify their manager' })).toBeInTheDocument()
    })

    it('offers neither on Leave Settings', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.queryByRole('switch', { name: 'Send year-end warning emails' })).not.toBeInTheDocument()
        expect(screen.queryByRole('switch', { name: 'Also notify their manager' })).not.toBeInTheDocument()
        // The rollover the group is left with is still set here.
        expect(screen.getByRole('switch', { name: 'Auto-run rollover on reset date' })).toBeEnabled()
    })

    it('points from the leave year to where they are set', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByText(/Warning emails are configured on Notification Settings/)).toBeInTheDocument()
    })

    it('reports them as scheduled while they are on', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Upcoming Schedule')

        // Both warning rows; the rollover and new-year rows carry their own badges.
        expect(screen.getAllByText('Scheduled')).toHaveLength(2)
    })

    it('reports them as off in the schedule when they are switched off', async () => {
        api.getAppSettings.mockResolvedValue({ ...SETTINGS, sendYearEndWarningEmails: false })
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Upcoming Schedule')

        expect(screen.queryByText('Scheduled')).not.toBeInTheDocument()
        expect(screen.getAllByText('Off')).toHaveLength(2)
        // The dates stay: this is when they would go out, not a claim that they will.
        expect(screen.getByText('30-day warning emails')).toBeInTheDocument()
    })
})

/*
 * "Notify managers of team expiries" CCs the manager on the year-end warning emails, so
 * it does nothing at all while those emails are switched off. It used to sit as a
 * fourth peer switch, freely settable with no hint that it was inert.
 */
describe('the manager CC is shown as a child of the emails it rides on', () => {
    it('is disabled while year-end warning emails are off', async () => {
        api.getAppSettings.mockResolvedValue({ ...SETTINGS, sendYearEndWarningEmails: false })
        renderPanel(<OrgSettingsPanel />)
        await screen.findByText('🔔 Notification Settings')

        expect(screen.getByRole('switch', { name: 'Also notify their manager' })).toBeDisabled()
    })

    it('is settable once they are on', async () => {
        renderPanel(<OrgSettingsPanel />)
        await screen.findByText('🔔 Notification Settings')

        expect(screen.getByRole('switch', { name: 'Also notify their manager' })).toBeEnabled()
    })
})

/*
 * The card's amber banner said "Changes take effect from the next rollover only" while
 * the card also held a timesheet deadline and a public-holiday country, both of which
 * apply immediately. The deferred field is the start month, and the banner now says so.
 */
describe('the deferred-change warning names the field it applies to', () => {
    it('warns about the start month, not about every change on the card', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        expect(screen.getByText(/Changing the start month takes effect from the/)).toBeInTheDocument()
        expect(screen.queryByText(/Changes take effect from the/)).not.toBeInTheDocument()
    })

    it('states the leave year the change will not affect', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        // The configured leave year starts in January, so it spans the calendar year.
        const year = new Date().getFullYear()
        expect(screen.getByText(
            new RegExp(`The current leave year \\(1 Jan ${year} – 31 Dec ${year}\\) is unaffected`),
        )).toBeInTheDocument()
    })
})

/*
 * The Upcoming Schedule card offered "▶ Run Rollover Manually" with no onClick at all:
 * the most consequential action an admin could take on this page, doing nothing when
 * pressed. Nothing performs a rollover yet — there is no command, endpoint or job — so
 * the button is gone until there is something for it to call.
 */
describe('no button offers an action nothing implements', () => {
    it('does not offer to run the rollover by hand', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Upcoming Schedule')

        expect(screen.queryByRole('button', { name: /Run Rollover/i })).not.toBeInTheDocument()
    })

    it('still shows the schedule the rollover is part of', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Upcoming Schedule')

        expect(screen.getByText('Year-end rollover')).toBeInTheDocument()
        expect(screen.getByText('New year opens')).toBeInTheDocument()
    })

    /* Every labelled button left on the page does something. Guards against the next
       dead control being added beside a live one. Icon-only buttons are excluded — the
       holiday picker's dropdown indicator is one, and it is wired by MUI. */
    it('leaves only buttons that are wired up', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Leave Year Configuration')

        const labelled = screen.getAllByRole('button')
            .map((b) => b.textContent?.trim())
            .filter((t): t is string => !!t)
        expect(labelled).toEqual(['Cancel', 'Save Settings', 'Reset working week & policy', 'Save Changes'])
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
        // Labelled "Weekends" when it moved here, though it is the working days it sets.
        expect(screen.getByText('Working days')).toBeInTheDocument()
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

/*
 * "Year Progress" was four equal saturated blocks labelled Q1–Q4 with a fixed-width
 * purple "Roll" sliver on the end: a legend, not a bar. It showed neither how much of
 * the leave year had gone nor where today fell — the one thing its title promised —
 * and the blocks ran green, green, amber, red, which reads as a severity scale for
 * what are just four quarters. It is a real progress bar now, and it reports the
 * elapsed share to a screen reader as well as on screen.
 */
describe('year progress shows how much of the leave year has gone', () => {
    beforeEach(() => {
        // Mid-year at noon, so the assertion below survives an off-by-one day either
        // way in how the elapsed share is counted. Only Date is faked: React Query and
        // waitFor still need real timers.
        vi.useFakeTimers({ toFake: ['Date'] })
        vi.setSystemTime(new Date(2026, 6, 2, 12))
    })
    afterEach(() => vi.useRealTimers())

    it('exposes the elapsed share as a progress bar', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Year Progress')

        const bar = screen.getByRole('progressbar', { name: /leave year progress/i })
        expect(bar).toHaveAttribute('aria-valuemin', '0')
        expect(bar).toHaveAttribute('aria-valuemax', '100')
        expect(bar).toHaveAttribute('aria-valuenow', '50')
        expect(screen.getByText('50% elapsed')).toBeInTheDocument()
    })

    it('marks the quarters without colouring them as a severity scale', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Year Progress')

        // The tick names the month its quarter opens in, which is the part that is
        // not obvious once the leave year does not start in January.
        expect(screen.getByText('Q1 Jan')).toBeInTheDocument()
        expect(screen.getByText('Q4 Oct')).toBeInTheDocument()
        // The old block strip, its month ranges and its rollover sliver.
        expect(screen.queryByText('Roll')).not.toBeInTheDocument()
        expect(screen.queryByText(/Q1 Jan–Mar/)).not.toBeInTheDocument()
    })
})

/*
 * 1 Jan 2027 was stated four times on one card: the Next Reset tile, a filled panel
 * explaining what the rollover does, and two schedule rows. The explanation belongs to
 * the event, so it sits on the schedule’s rollover row and nowhere else.
 */
describe('the rollover is explained once, on the event it explains', () => {
    it('carries the explanation on the rollover event itself', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText('Upcoming Schedule')

        const schedule = screen.getByRole('list', { name: /upcoming schedule/i })
        const events = within(schedule).getAllByRole('listitem')
        const rollover = events.find((li) => li.textContent?.includes('Year-end rollover'))

        expect(rollover).toHaveTextContent('auto-calculate carryover (max 5 days)')
        // And only there — it had a filled panel of its own, beside the tile that
        // already stated the date.
        expect(screen.getAllByText(/auto-calculate carryover/)).toHaveLength(1)
    })
})

/*
 * Three worked examples used to sit above this table, restating the same formula for
 * the under-cap, at-cap and over-cap case, above a table that does that arithmetic on
 * every row already. What an admin opens the card for is the total about to be lost
 * and who is losing it, so that is what it states — and the rows at risk sort to the
 * top, where ordering by name used to bury them.
 */
describe('unused leave leads with what is about to be lost', () => {
    const profile = (displayName: string, leaveBalance: number) => ({
        id: displayName, userId: displayName, displayName,
        departmentId: null, managerId: null,
        annualLeaveEntitlement: 25, leaveBalance,
        jobTitle: null, createdAt: '2026-01-01T00:00:00Z',
    })

    beforeEach(() => {
        /* The cap is 5, so 12 unused days lose 7 and 9 unused lose 4, while 3 unused
           lose nothing. Deliberately not in name order, and not in loss order either. */
        api.getEmployeeProfiles.mockResolvedValue([
            profile('Anna Zeta', 3), profile('Bob Alpha', 12), profile('Cara Beta', 9),
        ])
    })

    it('totals the days at risk and the people losing them', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText(/Unused Leave — End of/)

        expect(screen.getByText('Would Expire').parentElement).toHaveTextContent('11 days')
        expect(screen.getByText('Employees Affected').parentElement).toHaveTextContent('2 of 3')
        expect(screen.getByText('Would Carry Over').parentElement).toHaveTextContent('13 days')
    })

    it('puts whoever loses most first', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText(/Unused Leave — End of/)

        const names = screen.getAllByRole('row').slice(1)
            .map((row) => row.querySelector('td')?.textContent)
        expect(names).toEqual(['Bob Alpha', 'Cara Beta', 'Anna Zeta'])
    })

    it('states the projection as a projection, and drops the worked examples', async () => {
        renderPanel(<AppSettingsPanel />)
        await screen.findByText(/Unused Leave — End of/)

        // No rollover command, endpoint or job exists, so nothing here has run.
        expect(screen.getByText(/Nothing here has happened yet/)).toBeInTheDocument()
        expect(screen.queryByText(/^Under cap/)).not.toBeInTheDocument()
        expect(screen.queryByText(/New balance = /)).not.toBeInTheDocument()
    })
})
