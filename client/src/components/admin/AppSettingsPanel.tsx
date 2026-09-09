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
const TD = { py: '11px', px: '14px', fontSize: 13, color: 'text.primary', borderBottom: '1px solid', borderColor: 'divider' }

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
function SectionLabel({ children, mb = 1 }: { children: React.ReactNode; mb?: number }) {
    return (
        <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb }}>
            {children}
        </Typography>
    )
}

/** The bordered well a group of switches sits in. */
const TOGGLE_GROUP = { border: '1px solid', borderColor: 'divider', borderRadius: '8px', px: 2, py: 0.5 } as const

/** A field group below the first, separated by a rule. */
const NEXT_GROUP = { borderTop: '1px solid', borderColor: 'divider', pt: 2 } as const

/* What the Organization card's Reset button restores. The holiday country is
   deliberately not in here: there is no default country, and clearing it would
   silently drop every public holiday. */
const ORG_DEFAULTS = {
    workingHoursStart: '09:00',
    workingHoursEnd: '18:00',
    timeZoneId: 'UTC',
    workingDays: 'mon-fri',
    weeklyHoursTarget: 40,
    timesheetSubmissionDeadlineDay: 'fri',
    timesheetSubmissionDeadlineTime: '18:00',
} satisfies Partial<AppSettings>

const ORG_DEFAULT_KEYS = Object.keys(ORG_DEFAULTS) as (keyof typeof ORG_DEFAULTS)[]

/* One figure in the leave-year summary. These were four tiles filled with four
   different semantic tints — amber over a carryover cap that is only a policy number,
   green over a day count that says nothing about health. The well is neutral now, and
   the one cell that takes a colour is the one figure that can turn urgent. */
function StatCell({ label, value, valueColor, divideLeft, divideTop }: {
    label: string; value: string; valueColor?: SxColor
    divideLeft: boolean; divideTop: boolean
}) {
    return (
        <Box sx={{
            px: 1.75, py: 1.4,
            borderLeft: divideLeft ? '1px solid' : 'none',
            borderTop: divideTop ? '1px solid' : 'none',
            borderColor: 'divider',
        }}>
            <Typography sx={{ fontSize: 10, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: 0.4 }}>
                {label}
            </Typography>
            <Typography sx={{ fontSize: 15, fontWeight: 700, color: valueColor ?? 'text.primary' }}>{value}</Typography>
        </Box>
    )
}

/* The leave year as a bar: how much of it has gone, and where the quarters begin.
   This was four equal saturated blocks labelled Q1–Q4 with a fixed-width purple
   "Roll" sliver on the end — a legend, not a bar. It marked neither today nor the
   elapsed share its own title promised, and it ran green→amber→red, which reads as a
   severity scale for what are only four quarters. Quarters are boundaries here: a
   tick and a label, no fill of their own. Today is the edge of the fill, marked. */
function YearProgress({ pct, ticks, startLabel, endLabel }: {
    pct: number; ticks: { label: string; at: number }[]; startLabel: string; endLabel: string
}) {
    const rounded = Math.round(pct)
    return (
        <Box>
            <Box sx={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', mb: 1 }}>
                <SectionLabel mb={0}>Year Progress</SectionLabel>
                <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary' }}>{rounded}% elapsed</Typography>
            </Box>
            {/* A bar that conveys a position should say so: the block strip it replaces
                had no role and no value, so a screen reader got four coloured divs. */}
            <Box
                role="progressbar" aria-label="Leave year progress"
                aria-valuemin={0} aria-valuemax={100} aria-valuenow={rounded}
                aria-valuetext={`${rounded}% of the leave year elapsed`}
                sx={{ position: 'relative', height: 8, borderRadius: '4px', bgcolor: 'action.hover', overflow: 'hidden' }}
            >
                <Box sx={{ position: 'absolute', top: 0, bottom: 0, left: 0, width: `${pct}%`, bgcolor: 'primary.main' }} />
                {ticks.filter((t) => t.at > 0).map((t) => (
                    <Box key={t.label} sx={{ position: 'absolute', top: 0, bottom: 0, left: `${t.at}%`, width: '1px', bgcolor: 'background.paper' }} />
                ))}
                {/* Today, at the fill's edge — the boundary is already visible, this puts
                    an exact mark on it. */}
                <Box sx={{ position: 'absolute', top: 0, bottom: 0, left: `calc(${pct}% - 1px)`, width: '2px', bgcolor: 'primary.dark' }} />
            </Box>
            <Box sx={{ position: 'relative', height: 15, mt: 0.4 }}>
                {ticks.map((t) => (
                    <Typography key={t.label} sx={{ position: 'absolute', left: `${t.at}%`, ml: '3px', fontSize: 10, fontWeight: 600, color: 'text.disabled', whiteSpace: 'nowrap' }}>
                        {t.label}
                    </Typography>
                ))}
            </Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', fontSize: 10, color: 'text.disabled' }}>
                <span>{startLabel}</span>
                <span>{endLabel}</span>
            </Box>
        </Box>
    )
}

/* One event on the year-end timeline. These were four filled colour blocks stacked on
   each other: four competing hues, no hierarchy, and nothing to say the list is simply
   chronological. The rail carries the order, the dot and the badge carry the state,
   and the rows are a real list, so a screen reader is told how many there are. */
function ScheduleEvent({ label, date, note, dot, filled, badge, badgeBg, badgeColor, last = false }: {
    label: string; date: string; note?: string
    dot: SxColor; filled?: boolean
    badge: string; badgeBg: SxColor; badgeColor: string; last?: boolean
}) {
    return (
        <Box component="li" sx={{ display: 'flex', gap: 1.5, pb: last ? 0 : 2 }}>
            {/* The rail runs from under this event's dot down to the next one's. */}
            <Box sx={{ position: 'relative', width: 9, flexShrink: 0 }}>
                {!last && <Box sx={{ position: 'absolute', top: 15, bottom: 0, left: 4, width: '1px', bgcolor: 'divider' }} />}
                <Box sx={{ width: 9, height: 9, mt: '4px', borderRadius: '50%', border: '2px solid', borderColor: dot, bgcolor: filled ? dot : 'background.paper' }} />
            </Box>
            <Box sx={{ flex: 1, minWidth: 0 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
                    <Typography sx={{ fontSize: 12.5, fontWeight: 600, color: 'text.primary' }}>{label}</Typography>
                    <Box component="span" sx={{ fontSize: 10.5, fontWeight: 500, px: 1, py: 0.3, borderRadius: '20px', bgcolor: badgeBg, color: badgeColor, whiteSpace: 'nowrap', flexShrink: 0 }}>
                        {badge}
                    </Box>
                </Box>
                <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>{date}</Typography>
                {note && <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.5, lineHeight: 1.5 }}>{note}</Typography>}
            </Box>
        </Box>
    )
}

/* One organization setting: its name, its control directly beneath, and what the
   setting drives under that. This was a label-left / control-right row, which is fine
   in a sidebar and unreadable in a card that spans the page — it left every input some
   700px from the label it belonged to, with nothing but whitespace pairing them. Laid
   out like the fields on the Leave Year card beside it. */
function Field({ label, hint, span = 3, children }: {
    label: string; hint: string; span?: number; children: React.ReactNode
}) {
    return (
        <Grid size={{ xs: 12, sm: 6, md: span }}>
            <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>{label}</Typography>
            {children}
            <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.5, lineHeight: 1.45 }}>{hint}</Typography>
        </Grid>
    )
}

/* Each card on this page saves only its own fields. Both cards edit one `form`, but a
   card's payload is built from the last *saved* settings and overridden with just that
   card's fields — so pressing Save in one card cannot quietly commit edits the admin
   left sitting in the other. Before this, both buttons posted the whole form. */
type SaveGroup = 'leaveYear' | 'organization'

const GROUP_FIELDS: Record<SaveGroup, readonly (keyof AppSettings)[]> = {
    // The two year-end warning-email flags were listed here while this card edited
    // them. They are set on Notification Settings now, so they are not this card's to
    // send: withFields leaves them at their last saved value.
    leaveYear: [
        'leaveYearStartMonth',
        'autoRunRollover',
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

    const resetOrgDefaults = () => setForm((prev) => ({ ...prev, ...ORG_DEFAULTS }))

    /* Nothing to restore, so the button says so rather than being pressable and inert.
       Note this is not the same question as whether the card is dirty: an admin can
       have unsaved edits that happen to be the defaults. */
    const atOrgDefaults = ORG_DEFAULT_KEYS.every((key) => form[key] === ORG_DEFAULTS[key])

    const nextReset = addDays(lyEnd, 1)
    const daysRemaining = Math.max(0, diffDays(now, lyEnd))
    const yearLabel = `${startYear}–${String(startYear + 1).slice(2)}`

    const warningDate = addDays(lyEnd, -YEAR_END_WARNING_DAYS)
    const finalWarnDate = addDays(lyEnd, -FINAL_WARNING_DAYS)

    /* Both warning rows are only pending if the emails are switched on — the switch is
       on Notification Settings now, and a row still badged "Scheduled" for an email
       nobody sends would be the schedule lying about the one thing it is for. The dates
       stay either way: they are when the emails would go out. */
    const warningsOn = form.sendYearEndWarningEmails
    const WARNING_EVENT = warningsOn
        ? { dot: 'warning.main', badge: 'Scheduled', badgeBg: softBg('warning'), badgeColor: 'warning.dark' }
        : { dot: 'divider', badge: 'Off', badgeBg: 'action.selected', badgeColor: 'text.secondary' }

    /* The leave year as an elapsed share plus its quarter boundaries, both measured in
       days from the start — so a leave year opening in a 31-day month puts the Q2 tick
       where Q2 begins, not at a flat quarter of the bar. */
    const yearProgress = useMemo(() => {
        const total = Math.max(1, diffDays(lyStart, addDays(lyEnd, 1)))
        const elapsed = Math.min(total, Math.max(0, diffDays(lyStart, now)))
        const m = form.leaveYearStartMonth - 1
        const ticks = [0, 1, 2, 3].map((q) => ({
            label: `Q${q + 1} ${MONTHS[(m + q * 3) % 12].slice(0, 3)}`,
            at: (diffDays(lyStart, new Date(lyStart.getFullYear(), m + q * 3, 1)) / total) * 100,
        }))
        return { pct: (elapsed / total) * 100, ticks }
    }, [lyStart, lyEnd, now, form.leaveYearStartMonth])

    /* The one figure in the summary that can turn urgent, and so the one that takes a
       colour. Everything else there is a date or a policy number. */
    const daysRemainingColor: SxColor | undefined =
        daysRemaining <= 7 ? 'error.dark' : daysRemaining <= 30 ? 'warning.dark' : undefined

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
            /* Most at risk first. Everyone reads the same while no leave is booked, so
               the ordering only shows itself in real data — which is when it matters. */
            .sort((a, b) => b.expires - a.expires || a.name.localeCompare(b.name)),
        [profiles, departmentNameById, carryoverCap, annualAllowance])

    /* What the table is actually scanned for: how much is about to be lost and by how
       many people. Three worked examples used to sit above it spelling out the
       arithmetic three times, over a table that already does the arithmetic per row. */
    const carryoverSummary = useMemo(() => ({
        expiring: carryoverRows.reduce((sum, r) => sum + r.expires, 0),
        carrying: carryoverRows.reduce((sum, r) => sum + r.carryover, 0),
        affected: carryoverRows.filter((r) => r.expires > 0).length,
    }), [carryoverRows])

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

                                {/* What happens by itself at the year end. The two year-end warning-email
                                    switches were here too; which emails go out is an email preference,
                                    so they sit with the rest of those on Notification Settings. The
                                    pointer is there because this card is where an admin comes looking,
                                    and the schedule beside it lists the emails themselves. */}
                                <Box sx={NEXT_GROUP}>
                                    <SectionLabel>Year-End Automation</SectionLabel>
                                    <Box sx={TOGGLE_GROUP}>
                                        <ToggleRow
                                            title="Auto-run rollover on reset date"
                                            sub={`Carries over, expires the excess and reopens balances on ${fmt(nextReset)}`}
                                            checked={form.autoRunRollover} onChange={(v) => set('autoRunRollover', v)} />
                                    </Box>
                                    <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.75 }}>
                                        Warning emails are configured on Notification Settings › Email Notifications
                                    </Typography>
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

                {/* ── Right: the year as it stands, and what happens to it ─────────
                     One card, not two. The schedule is this same leave year's timeline,
                     so splitting it into a card of its own bought nothing and left the
                     configuration column beside it ending in a stretch of empty page. */}
                <Grid size={{ xs: 12, md: 5 }}>
                    <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                        <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Current Leave Year</Typography>
                            <Box component="span" sx={{ fontSize: 11, fontWeight: 500, px: 1.1, py: 0.4, borderRadius: '20px', bgcolor: softBg('success'), color: 'success.dark' }}>● Active</Box>
                        </Box>
                        <Box sx={{ p: 2.25 }}>
                            <Stack spacing={2}>
                                {/* The four figures, in one well rather than four coloured tiles */}
                                <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', border: '1px solid', borderColor: 'divider', borderRadius: '8px', overflow: 'hidden' }}>
                                    {([
                                        { label: 'Year', value: yearLabel },
                                        { label: 'Days Remaining', value: String(daysRemaining), valueColor: daysRemainingColor },
                                        { label: 'Carryover Cap', value: `${carryoverCap} days` },
                                        { label: 'Next Reset', value: fmt(nextReset) },
                                    ] as { label: string; value: string; valueColor?: SxColor }[]).map((stat, idx) => (
                                        <StatCell key={stat.label} {...stat} divideLeft={idx % 2 === 1} divideTop={idx > 1} />
                                    ))}
                                </Box>

                                <YearProgress pct={yearProgress.pct} ticks={yearProgress.ticks}
                                    startLabel={fmt(lyStart)} endLabel={fmt(lyEnd)} />

                                {/* What is coming, in the order it comes. The rollover row carries the
                                    sentence that had a filled panel of its own, three restatements of
                                    the reset date away from the event it describes. */}
                                <Box sx={NEXT_GROUP}>
                                    <SectionLabel>Upcoming Schedule</SectionLabel>
                                    <Box component="ul" aria-label="Upcoming schedule" sx={{ listStyle: 'none', m: 0, p: 0 }}>
                                        <ScheduleEvent label={`${YEAR_END_WARNING_DAYS}-day warning emails`} date={fmt(warningDate)} {...WARNING_EVENT} />
                                        <ScheduleEvent label={`${FINAL_WARNING_DAYS}-day final warning`} date={fmt(finalWarnDate)} {...WARNING_EVENT} />
                                        <ScheduleEvent
                                            label="Year-end rollover" date={`${fmt(nextReset)} · midnight`}
                                            note={`Will auto-calculate carryover (max ${carryoverCap} days), expire excess, and reset all balances.`}
                                            dot="error.main" filled badge="Year End" badgeBg={softBg('error')} badgeColor="error.dark" />
                                        <ScheduleEvent
                                            label="New year opens" date={fmt(nextReset)}
                                            dot="success.main" badge="New Year" badgeBg={softBg('success')} badgeColor="success.dark" last />
                                        {/* A "▶ Run Rollover Manually" button sat here with no onClick — it
                                            offered an admin the single most consequential action on the page
                                            and did nothing at all when pressed. Nothing performs a rollover
                                            yet: there is no command, endpoint or job for it anywhere, and
                                            AutoRunRollover above is a stored flag nothing acts on either.
                                            Restore the button when a rollover command exists to call. */}
                                    </Box>
                                </Box>
                            </Stack>
                        </Box>
                    </Box>
                </Grid>
            </Grid>

            {/* ── Organization ──────────────────────────────────────────────────
                 Moved off Reminders & Notifications: the working week and the office
                 clock are org-wide configuration, not notification preferences. The
                 financial-year row did not come with them — see the leave year above. */}
            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    {/* Named for what it holds. "Organization" was also the title of the
                        page this card sits on — see Sidebar's admin item and Topbar's page
                        title — so the header repeated its own container. */}
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Working Week &amp; Policy</Typography>
                    <Box component="span" sx={{ fontSize: 11, fontWeight: 500, px: 1.1, py: 0.4, borderRadius: '20px', bgcolor: softBg('info'), color: 'info.dark' }}>Admin Only</Box>
                </Box>
                <Box sx={{ p: 2.25 }}>
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', mb: 2 }}>
                        Everything here applies immediately, to everyone — none of it waits for the leave-year rollover.
                    </Typography>

                    <SectionLabel>Working Week</SectionLabel>
                    <Grid container spacing={2}>
                        <Field label="Working hours start" hint="Check-in alerts and attendance reports">
                            <TextField type="time" size="small" fullWidth value={form.workingHoursStart}
                                onChange={(e) => set('workingHoursStart', e.target.value)}
                                inputProps={{ 'aria-label': 'Working hours start' }} sx={{ '& .MuiInputBase-input': { fontSize: 13 } }} />
                        </Field>
                        <Field label="Working hours end" hint="Ends the default work day">
                            <TextField type="time" size="small" fullWidth value={form.workingHoursEnd}
                                onChange={(e) => set('workingHoursEnd', e.target.value)}
                                inputProps={{ 'aria-label': 'Working hours end' }} sx={{ '& .MuiInputBase-input': { fontSize: 13 } }} />
                        </Field>
                        <Field label="Timezone" hint="All time-based calculations">
                            <Select size="small" fullWidth value={form.timeZoneId}
                                onChange={(e) => set('timeZoneId', e.target.value)}
                                inputProps={{ 'aria-label': 'Timezone' }} sx={{ fontSize: 13 }}>
                                {TIMEZONES.map((tz) => <MenuItem key={tz} value={tz} sx={{ fontSize: 13 }}>{tz}</MenuItem>)}
                            </Select>
                        </Field>
                        {/* Labelled "Weekends" until now, while its own description said it
                            defines the working days and its value read "Monday – Friday". It
                            sets the working days; the weekend is what is left. */}
                        <Field label="Working days" hint="Everything else counts as a weekend">
                            <Select size="small" fullWidth value={form.workingDays}
                                onChange={(e) => set('workingDays', e.target.value)}
                                inputProps={{ 'aria-label': 'Working days' }} sx={{ fontSize: 13 }}>
                                {WORKING_DAYS.map((w) => <MenuItem key={w.value} value={w.value} sx={{ fontSize: 13 }}>{w.label}</MenuItem>)}
                            </Select>
                        </Field>

                        {form.workingDays === 'custom' && (() => {
                            const selected = (form.workingDaysCustom ?? '').split(',').map((t) => t.trim()).filter(Boolean)
                            return (
                                <Grid size={12}>
                                    <SectionLabel>Custom working days</SectionLabel>
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
                                </Grid>
                            )
                        })()}
                    </Grid>

                    {/* Moved off the leave-year card, whose title did not cover either of them
                        and whose "next rollover only" banner was wrong about both. */}
                    <Box sx={{ ...NEXT_GROUP, mt: 2.5 }}>
                        <SectionLabel>Timesheet Policy</SectionLabel>
                        <Grid container spacing={2}>
                            <Field label="Weekly hours target" hint="Drives the under/over-target colouring on All Timesheets">
                                <TextField
                                    type="number" size="small" fullWidth
                                    value={form.weeklyHoursTarget}
                                    onChange={(e) => set('weeklyHoursTarget', Math.min(168, Math.max(1, Number(e.target.value))))}
                                    inputProps={{ min: 1, max: 168, 'aria-label': 'Weekly hours target' }} sx={{ '& .MuiInputBase-input': { fontSize: 13 } }} />
                            </Field>
                            <Field label="Submission deadline" span={6}
                                hint="Sets the on-time/late flag on All Timesheets. Evaluated in UTC.">
                                <Box sx={{ display: 'flex', gap: 1.5 }}>
                                    <Select size="small" value={form.timesheetSubmissionDeadlineDay}
                                        onChange={(e) => set('timesheetSubmissionDeadlineDay', String(e.target.value))}
                                        inputProps={{ 'aria-label': 'Submission deadline day' }}
                                        sx={{ fontSize: 13, flex: 1 }}>
                                        {WEEKDAYS.map(([token, label]) => <MenuItem key={token} value={token} sx={{ fontSize: 13 }}>{label}</MenuItem>)}
                                    </Select>
                                    <TextField type="time" size="small"
                                        value={form.timesheetSubmissionDeadlineTime}
                                        onChange={(e) => set('timesheetSubmissionDeadlineTime', e.target.value)}
                                        inputProps={{ 'aria-label': 'Submission deadline time' }}
                                        sx={{ '& .MuiInputBase-input': { fontSize: 13 }, flex: 1 }} />
                                </Box>
                            </Field>
                        </Grid>
                    </Box>

                    <Box sx={{ ...NEXT_GROUP, mt: 2.5 }}>
                        <SectionLabel>Public Holidays</SectionLabel>
                        <Grid container spacing={2}>
                            <Field label="Holiday calendar" span={6}
                                hint="Fetched from date.nager.at and cached server-side. Changing country re-fetches on the next request.">
                                <Autocomplete<HolidayCountry, false, false, false>
                                    size="small" fullWidth
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
                                    renderInput={(params) => (
                                        <TextField
                                            {...params}
                                            placeholder="Select a country for public holidays"
                                            inputProps={{ ...params.inputProps, 'aria-label': 'Holiday calendar' }}
                                            sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                        />
                                    )}
                                />
                            </Field>
                        </Grid>
                    </Box>

                    {errorGroup === 'organization' && (
                        <Alert severity="error" sx={{ mt: 2 }}>{getApiErrorMessage(mutation.error, 'Failed to save settings.')}</Alert>
                    )}
                    {savedGroup === 'organization' && <Alert severity="success" sx={{ mt: 2 }}>Working week &amp; policy saved.</Alert>}

                    {/* "Reset to defaults" until now — it has never touched the holiday
                        country, for the reason given on ORG_DEFAULTS, so it promised the whole
                        card and restored two thirds of it. */}
                    <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end', pt: 2, mt: 2.5, borderTop: '1px solid', borderColor: 'divider' }}>
                        <Button variant="outlined" size="small" onClick={resetOrgDefaults} disabled={atOrgDefaults || mutation.isPending}
                            sx={{ textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}>
                            Reset working week & policy
                        </Button>
                        <Button variant="contained" size="small" onClick={() => mutation.mutate('organization')} disabled={!isGroupDirty('organization') || mutation.isPending || customDaysInvalid}
                            startIcon={pendingGroup === 'organization' ? <CircularProgress size={13} color="inherit" /> : null}
                            sx={{ textTransform: 'none', boxShadow: 'none' }}>
                            {pendingGroup === 'organization' ? 'Saving…' : 'Save Changes'}
                        </Button>
                    </Box>
                </Box>
            </Box>

            {/* ── Unused leave at the year end ──────────────────────────────────
                 Titled "Carryover Preview" while nothing performs a carryover: there is
                 no rollover command, endpoint or job anywhere, which is why the manual
                 rollover button was removed from the card above. So this states what a
                 rollover *would* do to today's balances, and says so. */}
            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider' }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                        Unused Leave — End of {yearLabel}
                    </Typography>
                </Box>

                <Box sx={{ p: 2.25 }}>
                    {/* Both figures were quoted in a single run-on line in the header, which
                        had to be read twice to be parsed. Two lines, one job each. */}
                    <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                        Measured against the {carryoverCap}-day carryover cap and an entitlement of {annualAllowance} days/year — both set on Leave Types, the entitlement unless a per-employee figure overrides it.
                    </Typography>
                    <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.5 }}>
                        Nothing here has happened yet: this is what a year-end rollover would do to today’s balances if no further leave is booked.
                    </Typography>

                    {carryoverRows.length > 0 && (
                        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', border: '1px solid', borderColor: 'divider', borderRadius: '8px', overflow: 'hidden', mt: 2 }}>
                            <StatCell label="Would Expire" value={`${carryoverSummary.expiring} days`}
                                valueColor={carryoverSummary.expiring > 0 ? 'error.dark' : undefined}
                                divideLeft={false} divideTop={false} />
                            <StatCell label="Employees Affected" value={`${carryoverSummary.affected} of ${carryoverRows.length}`}
                                divideLeft divideTop={false} />
                            <StatCell label="Would Carry Over" value={`${carryoverSummary.carrying} days`}
                                divideLeft divideTop={false} />
                        </Box>
                    )}
                </Box>

                <Box sx={{ overflowX: 'auto' }}>
                    <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                        <TableHead>
                            <TableRow>
                                <TableCell sx={TH}>Employee</TableCell>
                                <TableCell sx={TH}>Dept</TableCell>
                                {/* "Closing Balance" until now, over EmployeeProfile.leaveBalance —
                                    which is the balance as it stands, not a projected closing one. */}
                                <TableCell sx={TH}>Unused Now</TableCell>
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
                                    <TableCell sx={{ ...TD, color: 'secondary.dark', fontWeight: 500 }}>
                                        {row.carryover > 0 ? `${row.carryover} days${row.expires > 0 ? ' (cap)' : ''}` : '—'}
                                    </TableCell>
                                    {/* The warning triangle was on every row that had a figure at all,
                                        so it flagged nothing. The red figure carries it. */}
                                    <TableCell sx={{ ...TD, color: row.expires > 0 ? 'error.main' : 'text.secondary', fontWeight: row.expires > 0 ? 500 : 400 }}>
                                        {row.expires > 0 ? `${row.expires} days` : '—'}
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
