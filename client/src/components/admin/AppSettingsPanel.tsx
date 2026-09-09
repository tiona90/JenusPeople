import { useState, useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Autocomplete from '@mui/material/Autocomplete'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Grid from '@mui/material/Grid'
import MenuItem from '@mui/material/MenuItem'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Typography from '@mui/material/Typography'
import { getAppSettings, getDepartments, getEmployeeProfiles, getHolidayCountries, getLeaveTypes, updateAppSettings } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { annualCarryoverCap, annualLeaveAllowance, employeeAnnualEntitlement } from '../../lib/leave-allowance'
import type { AppSettings, HolidayCountry } from '../../lib/types'
import { softBg, type SxColor } from '../../lib/theme-tokens'


const TH = {
    py: '10px', px: '14px', fontSize: 11, fontWeight: 600, color: 'text.secondary',
    textTransform: 'uppercase' as const, letterSpacing: '0.05em',
    bgcolor: 'action.hover', borderBottom: '1px solid', borderColor: 'divider',
}
const TD = { py: '11px', px: '14px', fontSize: 13, color: 'text.primary', borderBottom: `1px solid #F3F4F6` }

const MONTHS = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
]

const TIMEZONES = ['UTC', 'UTC-5 (Eastern)', 'UTC-6 (Central)', 'UTC-7 (Mountain)', 'UTC-8 (Pacific)']

/* When the two year-end warnings go out, relative to the leave year end. These were
   AppSettings columns edited on this page, and the schedule preview below was the only
   thing that read them — the emails they described are not scheduled from a setting,
   so the inputs moved a caption and nothing else. Fixed here until something sends
   them. See migration RemoveAppSettingsWarningDays. */
const YEAR_END_WARNING_DAYS = 30
const FINAL_WARNING_DAYS = 7

const WORKING_DAYS: { value: string; label: string }[] = [
    { value: 'mon-fri',  label: 'Monday – Friday (5-day week)' },
    { value: 'mon-sat',  label: 'Monday – Saturday (6-day week)' },
    { value: 'sun-fri',  label: 'Sunday – Friday (custom)' },
    { value: 'custom',   label: 'Custom days' },
]

// Day tokens in week order, for the "Custom days" picker. Tokens match the
// backend contract (lowercase 3-letter, stored as a CSV).
const CUSTOM_DAYS: { token: string; label: string }[] = [
    { token: 'mon', label: 'Mon' },
    { token: 'tue', label: 'Tue' },
    { token: 'wed', label: 'Wed' },
    { token: 'thu', label: 'Thu' },
    { token: 'fri', label: 'Fri' },
    { token: 'sat', label: 'Sat' },
    { token: 'sun', label: 'Sun' },
]

// Week tokens (matching the backend) in Monday-first order, with display labels.
const WEEKDAYS: [string, string][] = [
    ['mon', 'Monday'], ['tue', 'Tuesday'], ['wed', 'Wednesday'], ['thu', 'Thursday'],
    ['fri', 'Friday'], ['sat', 'Saturday'], ['sun', 'Sunday'],
]

function getLeaveYearBounds(startMonth: number, referenceDate = new Date()) {
    const m = startMonth - 1
    const startYear = referenceDate.getMonth() >= m ? referenceDate.getFullYear() : referenceDate.getFullYear() - 1
    const lyStart = new Date(startYear, m, 1)
    const lyEnd = new Date(startYear + 1, m, 0)
    return { lyStart, lyEnd, startYear }
}

function fmt(d: Date) {
    return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
}

function addDays(d: Date, n: number) {
    const r = new Date(d)
    r.setDate(r.getDate() + n)
    return r
}

function diffDays(a: Date, b: Date) {
    return Math.ceil((b.getTime() - a.getTime()) / 86400000)
}

/* A switch that only does something while another switch is on is shown as its child:
   indented, and greyed out and unclickable while the parent is off. The stored value is
   left alone — turning the parent back on restores the choice that was made. */
function ToggleRow({ title, sub, checked, onChange, disabled = false, indent = false }: {
    title: string; sub: string; checked: boolean; onChange: (v: boolean) => void
    disabled?: boolean; indent?: boolean
}) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2, py: 1.25, pl: indent ? 2.5 : 0, opacity: disabled ? 0.45 : 1, borderBottom: '1px solid', borderColor: 'divider', '&:last-child': { borderBottom: 'none' } }}>
            <Box>
                <Typography sx={{ fontSize: 13, fontWeight: 500, color: 'text.primary' }}>
                    {indent && <Box component="span" sx={{ color: 'text.disabled', mr: 0.75 }}>↳</Box>}
                    {title}
                </Typography>
                <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>{sub}</Typography>
            </Box>
            {/* Without a label the switch has no accessible name at all — the title beside
                it is a plain Typography, not a <label>. Two MUI 7 traps here: `inputProps`
                is ignored (it must be `slotProps.input`), and `slotProps.input` *replaces*
                the defaults rather than merging, so role="switch" has to be restated. */}
            <Switch checked={checked} onChange={(e) => onChange(e.target.checked)} size="small" disabled={disabled}
                slotProps={{ input: { role: 'switch', 'aria-label': title } }} />
        </Box>
    )
}

/* Every group of fields carries one of these, so no group is left to be told apart by
   spacing alone. Written in Title Case, not upper — the uppercasing is CSS, and the
   text here is what a screen reader and a test both read. */
function SectionLabel({ children }: { children: React.ReactNode }) {
    return (
        <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: 1 }}>
            {children}
        </Typography>
    )
}

/** The bordered well a group of switches sits in. */
const TOGGLE_GROUP = { border: '1px solid', borderColor: 'divider', borderRadius: '8px', px: 2, py: 0.5 } as const

/** A field group below the first, separated by a rule. */
const NEXT_GROUP = { borderTop: '1px solid', borderColor: 'divider', pt: 2 } as const

function ScheduleRow({ label, date, color, bg, border, badge, badgeBg, badgeColor }: {
    label: string; date: string; color: string; bg: SxColor; border: string
    badge: string; badgeBg: SxColor; badgeColor: string
}) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', p: '10px 12px', bgcolor: bg, border: '1px solid', borderColor: border, borderRadius: '8px' }}>
            <Box>
                <Typography sx={{ fontSize: 12, fontWeight: 500, color }}>{label}</Typography>
                <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>{date}</Typography>
            </Box>
            <Box component="span" sx={{ fontSize: 11, fontWeight: 500, px: 1.1, py: 0.4, borderRadius: '20px', bgcolor: badgeBg, color: badgeColor, whiteSpace: 'nowrap' }}>
                {badge}
            </Box>
        </Box>
    )
}

function SettingRow({ label, desc, control }: { label: string; desc: string; control: React.ReactNode }) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 2, py: 1.5, borderBottom: '1px solid', borderColor: 'divider', '&:last-of-type': { borderBottom: 'none' } }}>
            <Box sx={{ flex: 1 }}>
                <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>{label}</Typography>
                <Typography sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.4 }}>{desc}</Typography>
            </Box>
            <Box sx={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: 1.5 }}>{control}</Box>
        </Box>
    )
}

/* Each card on this page saves only its own fields. Both cards edit one `form`, but a
   card's payload is built from the last *saved* settings and overridden with just that
   card's fields — so pressing Save in one card cannot quietly commit edits the admin
   left sitting in the other. Before this, both buttons posted the whole form. */
type SaveGroup = 'leaveYear' | 'organization'

const GROUP_FIELDS: Record<SaveGroup, readonly (keyof AppSettings)[]> = {
    leaveYear: [
        'leaveYearStartMonth',
        'autoRunRollover',
        'sendYearEndWarningEmails',
        'notifyManagersOfTeamExpiries',
        'blockLeaveSpanningIntoNextYear',
    ],
    organization: [
        'workingHoursStart', 'workingHoursEnd', 'timeZoneId', 'workingDays', 'workingDaysCustom',
        'weeklyHoursTarget', 'timesheetSubmissionDeadlineDay', 'timesheetSubmissionDeadlineTime',
        'holidayCountryCode', 'holidayCountryName',
    ],
}

/** `base`, with `fields` taken from `source`. */
function withFields(base: AppSettings, source: AppSettings, fields: readonly (keyof AppSettings)[]): AppSettings {
    const next: AppSettings = { ...base }
    for (const key of fields) Object.assign(next, { [key]: source[key] })
    return next
}

const DEFAULT: AppSettings = {
    leaveYearStartMonth: 1,
    autoRunRollover: true,
    sendYearEndWarningEmails: true,
    blockLeaveSpanningIntoNextYear: true,
    notifyManagersOfTeamExpiries: true,
    holidayCountryCode: null,
    holidayCountryName: null,
    workingHoursStart: '09:00',
    workingHoursEnd: '18:00',
    timeZoneId: 'UTC',
    financialYearStartMonth: 1,
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
    reminders: [],
}

export default function AppSettingsPanel() {
    const queryClient = useQueryClient()
    const now = useMemo(() => new Date(), [])

    const { data: saved, isLoading } = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: departments = [] } = useQuery({ queryKey: ['departments'], queryFn: getDepartments })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const departmentNameById = useMemo(
        () => new Map(departments.map((d) => [d.id, d.name])),
        [departments],
    )
    const { data: countries = [], isLoading: isLoadingCountries } = useQuery({
        queryKey: ['holidayCountries'],
        queryFn: getHolidayCountries,
        staleTime: 24 * 60 * 60 * 1000, // 1 day
    })

    const [form, setForm] = useState<AppSettings>(DEFAULT)
    const [savedGroup, setSavedGroup] = useState<SaveGroup | null>(null)

    // Sync the loaded settings into editable form state. Adjusted during render
    // (not an effect) per
    // https://react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes
    const [prevSaved, setPrevSaved] = useState(saved)
    if (saved !== prevSaved) {
        setPrevSaved(saved)
        if (saved) setForm(saved)
    }

    const set = <K extends keyof AppSettings>(key: K, val: AppSettings[K]) =>
        setForm(f => ({ ...f, [key]: val }))

    /* FinancialYearStartMonth used to be edited separately, on Reminders &
       Notifications, as "Financial year · When leave allocations reset" — the same
       concept as the leave year, in a second column nothing reads. The leave year is
       now the only control, and saving mirrors it so the column cannot drift. */
    const baseline = saved ?? DEFAULT

    const payloadFor = (group: SaveGroup): AppSettings => {
        const next = withFields(baseline, form, GROUP_FIELDS[group])
        return { ...next, financialYearStartMonth: next.leaveYearStartMonth }
    }

    const mutation = useMutation({
        mutationFn: (group: SaveGroup) => updateAppSettings(payloadFor(group)),
        onSuccess: (data, group) => {
            queryClient.setQueryData(['appSettings'], data)
            /* Refresh only the group that was saved. Adopting the response wholesale —
               which the render-time sync below would otherwise do, since `saved` just
               changed identity — would throw away the other card's unsaved edits. */
            setPrevSaved(data)
            setForm((f) => ({
                ...withFields(f, data, GROUP_FIELDS[group]),
                financialYearStartMonth: data.financialYearStartMonth,
            }))
            setSavedGroup(group)
            setTimeout(() => setSavedGroup((g) => (g === group ? null : g)), 3000)
        },
    })

    /** Which card, if any, is mid-save or has just failed — so its own card shows it. */
    const pendingGroup = mutation.isPending ? mutation.variables : undefined
    const errorGroup = mutation.isError ? mutation.variables : undefined

    const isGroupDirty = (group: SaveGroup) =>
        GROUP_FIELDS[group].some((key) => form[key] !== baseline[key])

    /** Discard just this card's edits, leaving the other card's alone. */
    const cancelGroup = (group: SaveGroup) =>
        setForm((f) => withFields(f, baseline, GROUP_FIELDS[group]))

    // ── Derived leave year data ───────────────────────────────────────────────
    const { lyStart, lyEnd, startYear } = useMemo(
        () => getLeaveYearBounds(form.leaveYearStartMonth, now),
        [form.leaveYearStartMonth, now])

    const customDaysInvalid =
        form.workingDays === 'custom' && (form.workingDaysCustom ?? '').split(',').filter(Boolean).length === 0

    /* The working week and the timesheet policy have defaults worth restoring. The
       holiday country does not — there is no default country, and clearing it would
       silently drop every public holiday. */
    const resetOrgDefaults = () =>
        setForm((prev) => ({
            ...prev,
            workingHoursStart: '09:00', workingHoursEnd: '18:00', timeZoneId: 'UTC', workingDays: 'mon-fri',
            weeklyHoursTarget: 40, timesheetSubmissionDeadlineDay: 'fri', timesheetSubmissionDeadlineTime: '18:00',
        }))

    const nextReset = addDays(lyEnd, 1)
    const daysRemaining = Math.max(0, diffDays(now, lyEnd))
    const yearLabel = `${startYear}–${String(startYear + 1).slice(2)}`

    const warningDate = addDays(lyEnd, -YEAR_END_WARNING_DAYS)
    const finalWarnDate = addDays(lyEnd, -FINAL_WARNING_DAYS)

    // Quarter labels based on start month
    const quarters = useMemo(() => {
        const m = form.leaveYearStartMonth - 1
        const qStart = (offset: number) => MONTHS[(m + offset) % 12].slice(0, 3)
        return [
            `Q1 ${qStart(0)}–${qStart(2)}`,
            `Q2 ${qStart(3)}–${qStart(5)}`,
            `Q3 ${qStart(6)}–${qStart(8)}`,
            `Q4 ${qStart(9)}–${qStart(11)}`,
        ]
    }, [form.leaveYearStartMonth])

    /* Both figures live on the leave type, which is the only place either is edited —
       the allowance and the cap that bounds it, in one row. This screen only quotes
       them, in the carryover preview below. See lib/leave-allowance.ts. */
    const annualAllowance = useMemo(() => annualLeaveAllowance(leaveTypes), [leaveTypes])
    const carryoverCap = useMemo(() => annualCarryoverCap(leaveTypes), [leaveTypes])

    // Carryover preview from real employee profiles
    const carryoverRows = useMemo(() =>
        profiles
            .filter(p => p.annualLeaveEntitlement > 0)
            .map(p => {
                const closing = Math.max(0, p.leaveBalance ?? 0)
                const carryover = Math.min(closing, carryoverCap)
                const expires = Math.max(0, closing - carryoverCap)
                // Each employee reopens on their own entitlement, not on one shared figure.
                const newBalance = carryover + employeeAnnualEntitlement(p, annualAllowance)
                // An Admin has no department, so the id can be absent as well as
                // unknown — both read as "—".
                const dept = (p.departmentId === null
                    ? undefined
                    : departmentNameById.get(p.departmentId)) ?? '—'
                return { name: p.displayName, dept, closing, carryover, expires, newBalance }
            })
            .sort((a, b) => a.name.localeCompare(b.name)),
        [profiles, departmentNameById, carryoverCap, annualAllowance])

    if (isLoading) return (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    )

    return (
        <Stack spacing={2.5}>
            <Grid container spacing={2.5} alignItems="flex-start">
                {/* ── Left: Configuration ─────────────────────────────────── */}
                <Grid size={{ xs: 12, md: 7 }}>
                    <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                        <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Leave Year Configuration</Typography>
                            <Box component="span" sx={{ fontSize: 11, fontWeight: 500, px: 1.1, py: 0.4, borderRadius: '20px', bgcolor: softBg('info'), color: 'info.dark' }}>Admin Only</Box>
                        </Box>
                        <Box sx={{ p: 2.25 }}>
                            <Stack spacing={2}>
                                {/* Names the one field it applies to. Sitting above the whole card, this
                                    used to say "Changes take effect from the next rollover only" over a
                                    timesheet deadline and a holiday country that both apply immediately. */}
                                <Box sx={{ display: 'flex', gap: 1, p: '10px 14px', bgcolor: softBg('warning'), border: '1px solid', borderColor: 'warning.main', borderRadius: '8px', fontSize: 12, color: 'warning.dark' }}>
                                    <span>⚠️</span>
                                    <span>
                                        Changing the start month takes effect from the <strong>next rollover only</strong>.
                                        The current leave year ({fmt(lyStart)} – {fmt(lyEnd)}) is unaffected.
                                    </span>
                                </Box>

                                {/* Leave year: one editable field, and the dates it derives */}
                                <Box>
                                    <SectionLabel>Leave Year</SectionLabel>
                                    <Grid container spacing={1.5}>
                                        <Grid size={{ xs: 12, sm: 6 }}>
                                            <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Leave Year Start Month</Typography>
                                            <Select
                                                size="small" fullWidth value={form.leaveYearStartMonth}
                                                onChange={(e) => set('leaveYearStartMonth', Number(e.target.value))}
                                                inputProps={{ 'aria-label': 'Leave year start month' }}
                                                sx={{ fontSize: 13 }}
                                            >
                                                {MONTHS.map((name, i) => <MenuItem key={i + 1} value={i + 1}>{name}</MenuItem>)}
                                            </Select>
                                            <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.5 }}>
                                                Also the financial year — when leave allocations reset
                                            </Typography>
                                        </Grid>
                                        {/* Derived, so it is stated rather than shown in a greyed-out input —
                                            a disabled text field reads as a control that is broken. */}
                                        <Grid size={{ xs: 12, sm: 6 }}>
                                            <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Leave Year Dates</Typography>
                                            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, minHeight: 40, fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                                                <span>{fmt(lyStart)}</span>
                                                <Box component="span" sx={{ color: 'text.disabled', fontWeight: 400 }}>→</Box>
                                                <span>{fmt(lyEnd)}</span>
                                            </Box>
                                            <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.5 }}>
                                                Follows the start month — nothing to set here
                                            </Typography>
                                        </Grid>
                                    </Grid>
                                </Box>

                                {/* The allowance and the cap that bounds it are columns on the leave type,
                                    set on Leave Types — this page used to edit both, and then quoted both
                                    here once it stopped. Neither is repeated in this card any more: the
                                    figures are stated where they are actually being applied, on the
                                    Carryover Preview below and the Carryover Cap tile, so a leave-year
                                    form does not restate a leave-type setting it cannot change. */}

                                {/* What happens by itself at the year end. The two lead times are stated
                                    here because the emails are the setting's whole effect — they were
                                    only legible in the Upcoming Schedule card, a column away. */}
                                <Box sx={NEXT_GROUP}>
                                    <SectionLabel>Year-End Automation</SectionLabel>
                                    <Box sx={TOGGLE_GROUP}>
                                        <ToggleRow
                                            title="Auto-run rollover on reset date"
                                            sub={`Carries over, expires the excess and reopens balances on ${fmt(nextReset)}`}
                                            checked={form.autoRunRollover} onChange={(v) => set('autoRunRollover', v)} />
                                        <ToggleRow
                                            title="Send year-end warning emails"
                                            sub={`Emails employees with days at risk, ${YEAR_END_WARNING_DAYS} and ${FINAL_WARNING_DAYS} days before year end`}
                                            checked={form.sendYearEndWarningEmails} onChange={(v) => set('sendYearEndWarningEmails', v)} />
                                        <ToggleRow
                                            indent disabled={!form.sendYearEndWarningEmails}
                                            title="Also notify their manager"
                                            sub="CC the employee's manager on those warning emails"
                                            checked={form.notifyManagersOfTeamExpiries} onChange={(v) => set('notifyManagersOfTeamExpiries', v)} />
                                    </Box>
                                </Box>

                                {/* Not automation — a rule applied when leave is requested. */}
                                <Box sx={NEXT_GROUP}>
                                    <SectionLabel>Leave Requests</SectionLabel>
                                    <Box sx={TOGGLE_GROUP}>
                                        <ToggleRow
                                            title="Block leave spanning into next year"
                                            sub={`Employees cannot request leave ending after ${fmt(lyEnd)}`}
                                            checked={form.blockLeaveSpanningIntoNextYear} onChange={(v) => set('blockLeaveSpanningIntoNextYear', v)} />
                                    </Box>
                                </Box>

                                {errorGroup === 'leaveYear' && (
                                    <Alert severity="error">{getApiErrorMessage(mutation.error, 'Failed to save settings.')}</Alert>
                                )}
                                {savedGroup === 'leaveYear' && <Alert severity="success">Leave year settings saved.</Alert>}

                                <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end' }}>
                                    <Button variant="outlined" size="small" onClick={() => cancelGroup('leaveYear')} disabled={!isGroupDirty('leaveYear') || mutation.isPending}
                                        sx={{ textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}>
                                        Cancel
                                    </Button>
                                    <Button variant="contained" size="small" onClick={() => mutation.mutate('leaveYear')} disabled={!isGroupDirty('leaveYear') || mutation.isPending}
                                        startIcon={pendingGroup === 'leaveYear' ? <CircularProgress size={13} color="inherit" /> : null}
                                        sx={{ textTransform: 'none', bgcolor: 'primary.main', '&:hover': { bgcolor: 'primary.dark' }, boxShadow: 'none' }}>
                                        {pendingGroup === 'leaveYear' ? 'Saving…' : 'Save Settings'}
                                    </Button>
                                </Box>
                            </Stack>
                        </Box>
                    </Box>
                </Grid>

                {/* ── Right: Status + Schedule ─────────────────────────────── */}
                <Grid size={{ xs: 12, md: 5 }}>
                    <Stack spacing={2}>
                        {/* Current Leave Year */}
                        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                            <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                                <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Current Leave Year</Typography>
                                <Box component="span" sx={{ fontSize: 11, fontWeight: 500, px: 1.1, py: 0.4, borderRadius: '20px', bgcolor: softBg('success'), color: 'success.dark' }}>● Active</Box>
                            </Box>
                            <Box sx={{ p: 2.25 }}>
                                {/* Stats 2×2 */}
                                <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px', mb: 2 }}>
                                    {([
                                        { bg: softBg('info'),      color: 'info.dark',      label: 'Year',           value: yearLabel },
                                        { bg: softBg('success'),   color: 'success.dark',   label: 'Days Remaining', value: String(daysRemaining) },
                                        { bg: softBg('warning'),   color: 'warning.dark',   label: 'Carryover Cap',  value: `${carryoverCap} days` },
                                        { bg: softBg('secondary'), color: 'secondary.dark', label: 'Next Reset',     value: fmt(nextReset) },
                                    ] as const).map(({ bg, color, label, value }) => (
                                        <Box key={label} sx={{ bgcolor: bg, borderRadius: '8px', p: '12px', textAlign: 'center' }}>
                                            <Typography sx={{ fontSize: 11, color, mb: 0.5 }}>{label}</Typography>
                                            <Typography sx={{ fontSize: 15, fontWeight: 700, color: 'text.primary' }}>{value}</Typography>
                                        </Box>
                                    ))}
                                </Box>

                                {/* Year progress bar */}
                                <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: 0.75 }}>
                                    Year Progress
                                </Typography>
                                <Box sx={{ display: 'flex', borderRadius: '6px', overflow: 'hidden', height: 26, mb: 0.5 }}>
                                    {[
                                        { label: quarters[0], color: 'primary.main' },
                                        { label: quarters[1], color: 'success.main' },
                                        { label: quarters[2], color: 'warning.main' },
                                        { label: quarters[3], color: 'error.main' },
                                    ].map(({ label, color }) => (
                                        <Box key={label} sx={{ flex: 1, bgcolor: color, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 10, color: '#fff', fontWeight: 500 }}>
                                            {label}
                                        </Box>
                                    ))}
                                    <Box sx={{ width: 36, bgcolor: '#8B5CF6', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 9, color: '#fff', fontWeight: 500 }}>
                                        Roll
                                    </Box>
                                </Box>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between', fontSize: 10, color: 'text.disabled', mb: 1.75 }}>
                                    <span>{fmt(lyStart)}</span>
                                    <span>{fmt(lyEnd)}</span>
                                </Box>

                                {/* Rollover info */}
                                <Box sx={{ display: 'flex', gap: 1, p: '10px 14px', bgcolor: softBg('secondary'), border: '1px solid', borderColor: 'secondary.main', borderRadius: '8px', fontSize: 12, color: 'secondary.dark' }}>
                                    <span>🔁</span>
                                    <span>On <strong>{fmt(nextReset)}</strong> the system will auto-calculate carryover (max {carryoverCap} days), expire excess, and reset all balances.</span>
                                </Box>
                            </Box>
                        </Box>

                        {/* Upcoming Schedule */}
                        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                            <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider' }}>
                                <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Upcoming Schedule</Typography>
                            </Box>
                            <Box sx={{ p: 2.25 }}>
                                <Stack spacing={1}>
                                    <ScheduleRow label={`${YEAR_END_WARNING_DAYS}-day warning emails`} date={fmt(warningDate)} color="warning.dark" bg={softBg('warning')} border="warning.main" badge="Scheduled" badgeBg={softBg('warning')} badgeColor="warning.dark" />
                                    <ScheduleRow label={`${FINAL_WARNING_DAYS}-day final warning`} date={fmt(finalWarnDate)} color="warning.dark" bg={softBg('warning')} border="warning.main" badge="Scheduled" badgeBg={softBg('warning')} badgeColor="warning.dark" />
                                    <ScheduleRow label="Year-end rollover" date={`${fmt(nextReset)} · midnight`} color="error.dark" bg={softBg('error')} border="error.main" badge="Year End" badgeBg={softBg('error')} badgeColor="error.dark" />
                                    <ScheduleRow label="New year opens" date={fmt(nextReset)} color="success.dark" bg={softBg('success')} border="success.main" badge="New Year" badgeBg={softBg('success')} badgeColor="success.dark" />
                                    {/* A "▶ Run Rollover Manually" button sat here with no onClick — it
                                        offered an admin the single most consequential action on the page
                                        and did nothing at all when pressed. Nothing performs a rollover
                                        yet: there is no command, endpoint or job for it anywhere, and
                                        AutoRunRollover above is a stored flag nothing acts on either.
                                        Restore the button when a rollover command exists to call. */}
                                </Stack>
                            </Box>
                        </Box>
                    </Stack>
                </Grid>
            </Grid>

            {/* ── Organization ──────────────────────────────────────────────────
                 Moved off Reminders & Notifications: the working week and the office
                 clock are org-wide configuration, not notification preferences. The
                 financial-year row did not come with them — see the leave year above. */}
            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', display: 'flex', alignItems: 'center', gap: 1 }}>
                        <Box component="span" sx={{ fontSize: 16 }}>🏢</Box>Organization
                    </Typography>
                    <Box component="span" sx={{ fontSize: 11, fontWeight: 500, px: 1.1, py: 0.4, borderRadius: '20px', bgcolor: softBg('info'), color: 'info.dark' }}>Admin Only</Box>
                </Box>
                <Box sx={{ p: 2.25 }}>
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', mb: 2 }}>
                        Everything here applies immediately, to everyone — none of it waits for the leave-year rollover.
                    </Typography>

                    <SectionLabel>Working Week</SectionLabel>
                    <SettingRow label="Working hours start" desc="Used for check-in alerts and attendance reports"
                        control={<TextField type="time" size="small" value={form.workingHoursStart} onChange={(e) => set('workingHoursStart', e.target.value)} inputProps={{ 'aria-label': 'Working hours start' }} sx={{ '& .MuiInputBase-input': { fontSize: 13 }, minWidth: 130 }} />} />
                    <SettingRow label="Working hours end" desc="Default work day ends"
                        control={<TextField type="time" size="small" value={form.workingHoursEnd} onChange={(e) => set('workingHoursEnd', e.target.value)} inputProps={{ 'aria-label': 'Working hours end' }} sx={{ '& .MuiInputBase-input': { fontSize: 13 }, minWidth: 130 }} />} />
                    <SettingRow label="Timezone" desc="Used for all time-based calculations"
                        control={<Select size="small" value={form.timeZoneId} onChange={(e) => set('timeZoneId', e.target.value)} inputProps={{ 'aria-label': 'Timezone' }} sx={{ fontSize: 13, minWidth: 190 }}>
                            {TIMEZONES.map((tz) => <MenuItem key={tz} value={tz} sx={{ fontSize: 13 }}>{tz}</MenuItem>)}
                        </Select>} />
                    <SettingRow label="Weekends" desc="Define which days are working days"
                        control={<Select size="small" value={form.workingDays} onChange={(e) => set('workingDays', e.target.value)} inputProps={{ 'aria-label': 'Weekends' }} sx={{ fontSize: 13, minWidth: 250 }}>
                            {WORKING_DAYS.map((w) => <MenuItem key={w.value} value={w.value} sx={{ fontSize: 13 }}>{w.label}</MenuItem>)}
                        </Select>} />

                    {form.workingDays === 'custom' && (() => {
                        const selected = (form.workingDaysCustom ?? '').split(',').map((t) => t.trim()).filter(Boolean)
                        return (
                            <Box sx={{ py: 1.5, borderTop: '1px solid', borderColor: 'divider' }}>
                                <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.04em', mb: 1 }}>
                                    Custom working days
                                </Typography>
                                <ToggleButtonGroup
                                    size="small"
                                    value={selected}
                                    onChange={(_, vals: string[]) =>
                                        set('workingDaysCustom', CUSTOM_DAYS.filter((d) => vals.includes(d.token)).map((d) => d.token).join(','))}
                                    sx={{ flexWrap: 'wrap' }}
                                >
                                    {CUSTOM_DAYS.map((d) => (
                                        <ToggleButton key={d.token} value={d.token} sx={{ textTransform: 'none', fontSize: 12, px: 1.75 }}>{d.label}</ToggleButton>
                                    ))}
                                </ToggleButtonGroup>
                                {selected.length === 0 && (
                                    <Typography sx={{ fontSize: 12, color: 'error.main', mt: 0.75 }}>Select at least one working day.</Typography>
                                )}
                            </Box>
                        )
                    })()}

                    {/* Moved off the leave-year card, whose title did not cover either of them
                        and whose "next rollover only" banner was wrong about both. */}
                    <Box sx={{ ...NEXT_GROUP, mt: 1 }}>
                        <SectionLabel>Timesheet Policy</SectionLabel>
                        <SettingRow label="Weekly hours target" desc="Drives the under/over-target colouring on the All Timesheets review page"
                            control={<TextField
                                type="number" size="small"
                                value={form.weeklyHoursTarget}
                                onChange={(e) => set('weeklyHoursTarget', Math.min(168, Math.max(1, Number(e.target.value))))}
                                inputProps={{ min: 1, max: 168, 'aria-label': 'Weekly hours target' }}
                                sx={{ '& .MuiInputBase-input': { fontSize: 13 }, minWidth: 130 }} />} />
                        <SettingRow label="Submission deadline" desc="Sets the on-time/late flag on All Timesheets. Evaluated in UTC."
                            control={<>
                                <Select size="small" value={form.timesheetSubmissionDeadlineDay}
                                    onChange={(e) => set('timesheetSubmissionDeadlineDay', String(e.target.value))}
                                    inputProps={{ 'aria-label': 'Submission deadline day' }}
                                    sx={{ fontSize: 13, minWidth: 140 }}>
                                    {WEEKDAYS.map(([token, label]) => <MenuItem key={token} value={token} sx={{ fontSize: 13 }}>{label}</MenuItem>)}
                                </Select>
                                <TextField type="time" size="small"
                                    value={form.timesheetSubmissionDeadlineTime}
                                    onChange={(e) => set('timesheetSubmissionDeadlineTime', e.target.value)}
                                    inputProps={{ 'aria-label': 'Submission deadline time' }}
                                    sx={{ '& .MuiInputBase-input': { fontSize: 13 }, minWidth: 130 }} />
                            </>} />
                    </Box>

                    <Box sx={{ ...NEXT_GROUP, mt: 1 }}>
                        <SectionLabel>Public Holidays</SectionLabel>
                        <SettingRow label="Holiday calendar" desc="Fetched from date.nager.at and cached server-side. Changing country re-fetches on first request."
                            control={<Autocomplete<HolidayCountry, false, false, false>
                                size="small"
                                loading={isLoadingCountries}
                                options={countries}
                                value={form.holidayCountryCode
                                    ? countries.find(c => c.countryCode === form.holidayCountryCode)
                                        ?? { countryCode: form.holidayCountryCode, name: form.holidayCountryName ?? form.holidayCountryCode }
                                    : null}
                                getOptionLabel={(o) => `${o.name} (${o.countryCode})`}
                                isOptionEqualToValue={(o, v) => o.countryCode === v.countryCode}
                                onChange={(_, val) => {
                                    setForm(f => ({
                                        ...f,
                                        holidayCountryCode: val?.countryCode ?? null,
                                        holidayCountryName: val?.name ?? null,
                                    }))
                                }}
                                sx={{ minWidth: 280 }}
                                renderInput={(params) => (
                                    <TextField
                                        {...params}
                                        placeholder="Select a country for public holidays"
                                        inputProps={{ ...params.inputProps, 'aria-label': 'Holiday calendar' }}
                                        sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                    />
                                )}
                            />} />
                    </Box>

                    {errorGroup === 'organization' && (
                        <Alert severity="error" sx={{ mt: 2 }}>{getApiErrorMessage(mutation.error, 'Failed to save settings.')}</Alert>
                    )}
                    {savedGroup === 'organization' && <Alert severity="success" sx={{ mt: 2 }}>Organization settings saved.</Alert>}

                    <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end', pt: 2, mt: 1, borderTop: '1px solid', borderColor: 'divider' }}>
                        <Button variant="outlined" size="small" onClick={resetOrgDefaults} disabled={mutation.isPending}
                            sx={{ textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}>
                            Reset to defaults
                        </Button>
                        <Button variant="contained" size="small" onClick={() => mutation.mutate('organization')} disabled={!isGroupDirty('organization') || mutation.isPending || customDaysInvalid}
                            startIcon={pendingGroup === 'organization' ? <CircularProgress size={13} color="inherit" /> : null}
                            sx={{ textTransform: 'none', boxShadow: 'none' }}>
                            {pendingGroup === 'organization' ? 'Saving…' : 'Save Changes'}
                        </Button>
                    </Box>
                </Box>
            </Box>

            {/* ── Carryover Preview ─────────────────────────────────────────── */}
            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                        Carryover Preview — End of {yearLabel}
                    </Typography>
                    <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                        {carryoverCap}-day max cap · entitlement {annualAllowance} days/year — both from Leave Types, entitlement unless set per employee
                    </Typography>
                </Box>

                {/* Example cards */}
                <Box sx={{ p: 2.25, pb: 0 }}>
                    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: '14px', mb: 2 }}>
                        {([
                            { bg: softBg('success'), border: 'success.main', color: 'success.dark', title: `Under cap (< ${carryoverCap} days unused)`, icon: '✅', body: 'All days carry over', sub: 'New balance = unused + entitlement' },
                            { bg: softBg('warning'), border: 'warning.main', color: 'warning.dark', title: `At cap (= ${carryoverCap} days unused)`, icon: '✅', body: `${carryoverCap} days carry (cap hit)`, sub: `New balance = ${carryoverCap} + entitlement` },
                            { bg: softBg('error'),   border: 'error.main',   color: 'error.dark',   title: `Over cap (> ${carryoverCap} days unused)`, icon: '⚠️', body: `${carryoverCap} carry · excess expires`, sub: `New balance = ${carryoverCap} + entitlement` },
                        ] as const).map(({ bg, border, color, title, icon, body, sub }) => (
                            <Box key={title} sx={{ bgcolor: bg, border: '1px solid', borderColor: border, borderRadius: '10px', p: '14px', textAlign: 'center' }}>
                                <Typography sx={{ fontSize: 11, color, fontWeight: 600, mb: 0.75 }}>{title}</Typography>
                                <Typography sx={{ fontSize: 18, fontWeight: 700, my: 0.5, color: 'text.primary' }}>{icon} {body}</Typography>
                                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>{sub}</Typography>
                            </Box>
                        ))}
                    </Box>
                </Box>

                <Box sx={{ overflowX: 'auto' }}>
                    <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                        <TableHead>
                            <TableRow>
                                <TableCell sx={TH}>Employee</TableCell>
                                <TableCell sx={TH}>Dept</TableCell>
                                <TableCell sx={TH}>Closing Balance</TableCell>
                                <TableCell sx={TH}>Carry Over</TableCell>
                                <TableCell sx={TH}>Expires</TableCell>
                                <TableCell sx={TH}>New Opening Balance</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {carryoverRows.length === 0 ? (
                                <TableRow>
                                    <TableCell colSpan={6} sx={{ ...TD, textAlign: 'center', color: 'text.disabled', py: 4 }}>
                                        No employee profiles found.
                                    </TableCell>
                                </TableRow>
                            ) : carryoverRows.map((row) => (
                                <TableRow key={row.name} sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}>
                                    <TableCell sx={TD}><strong>{row.name}</strong></TableCell>
                                    <TableCell sx={TD}>
                                        <Box component="span" sx={{ fontSize: 11, px: 1, py: 0.3, bgcolor: softBg('info'), color: 'info.dark', borderRadius: '4px', fontWeight: 500 }}>{row.dept}</Box>
                                    </TableCell>
                                    <TableCell sx={TD}>{row.closing} days</TableCell>
                                    <TableCell sx={{ ...TD, color: '#7C3AED', fontWeight: 500 }}>
                                        {row.carryover > 0 ? `${row.carryover} days${row.expires > 0 ? ' (cap)' : ''}` : '—'}
                                    </TableCell>
                                    <TableCell sx={{ ...TD, color: row.expires > 0 ? 'error.main' : 'text.secondary', fontWeight: row.expires > 0 ? 500 : 400 }}>
                                        {row.expires > 0 ? `${row.expires} days ⚠️` : '—'}
                                    </TableCell>
                                    <TableCell sx={{ ...TD, fontWeight: 600 }}>{row.newBalance} days</TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </Box>
            </Box>
        </Stack>
    )
}
