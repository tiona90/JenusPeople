import { useState, useEffect, useMemo } from 'react'
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
import Typography from '@mui/material/Typography'
import { getAppSettings, getDepartments, getEmployeeProfiles, getHolidayCountries, updateAppSettings } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
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

function ToggleRow({ title, sub, checked, onChange }: {
    title: string; sub: string; checked: boolean; onChange: (v: boolean) => void
}) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', py: 1.25, borderBottom: '1px solid', borderColor: 'divider', '&:last-child': { borderBottom: 'none' } }}>
            <Box>
                <Typography sx={{ fontSize: 13, fontWeight: 500, color: 'text.primary' }}>{title}</Typography>
                <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>{sub}</Typography>
            </Box>
            <Switch checked={checked} onChange={(e) => onChange(e.target.checked)} size="small" />
        </Box>
    )
}

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

const DEFAULT: AppSettings = {
    leaveYearStartMonth: 1,
    maxCarryoverDays: 5,
    defaultAnnualEntitlement: 20,
    yearEndWarningDays: 30,
    finalWarningDays: 7,
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
    const [showSaved, setShowSaved] = useState(false)

    useEffect(() => {
        if (saved) setForm(saved)
    }, [saved])

    const set = <K extends keyof AppSettings>(key: K, val: AppSettings[K]) =>
        setForm(f => ({ ...f, [key]: val }))

    const mutation = useMutation({
        mutationFn: () => updateAppSettings(form),
        onSuccess: (data) => {
            queryClient.setQueryData(['appSettings'], data)
            setShowSaved(true)
            setTimeout(() => setShowSaved(false), 3000)
        },
    })

    // ── Derived leave year data ───────────────────────────────────────────────
    const { lyStart, lyEnd, startYear } = useMemo(
        () => getLeaveYearBounds(form.leaveYearStartMonth, now),
        [form.leaveYearStartMonth, now])

    const endDate = useMemo(
        () => getLeaveYearBounds(form.leaveYearStartMonth, now).lyEnd,
        [form.leaveYearStartMonth, now])

    const nextReset = addDays(lyEnd, 1)
    const daysRemaining = Math.max(0, diffDays(now, lyEnd))
    const yearLabel = `${startYear}–${String(startYear + 1).slice(2)}`

    const warningDate = addDays(lyEnd, -form.yearEndWarningDays)
    const finalWarnDate = addDays(lyEnd, -form.finalWarningDays)

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

    // Carryover preview from real employee profiles
    const carryoverRows = useMemo(() =>
        profiles
            .filter(p => p.annualLeaveEntitlement > 0)
            .map(p => {
                const closing = Math.max(0, p.leaveBalance ?? 0)
                const carryover = Math.min(closing, form.maxCarryoverDays)
                const expires = Math.max(0, closing - form.maxCarryoverDays)
                const newBalance = carryover + form.defaultAnnualEntitlement
                return { name: p.displayName, dept: departmentNameById.get(p.departmentId) ?? '—', closing, carryover, expires, newBalance }
            })
            .sort((a, b) => a.name.localeCompare(b.name)),
        [profiles, departmentNameById, form.maxCarryoverDays, form.defaultAnnualEntitlement])

    const isDirty = JSON.stringify(form) !== JSON.stringify(saved ?? DEFAULT)

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
                                {/* Warning */}
                                <Box sx={{ display: 'flex', gap: 1, p: '10px 14px', bgcolor: softBg('warning'), border: '1px solid #FDE68A', borderRadius: '8px', fontSize: 12, color: 'warning.dark' }}>
                                    <span>⚠️</span>
                                    <span>Changes take effect from the <strong>next rollover only</strong>. The current year is not affected.</span>
                                </Box>

                                {/* Leave Year Dates */}
                                <Grid container spacing={1.5}>
                                    <Grid size={{ xs: 12, sm: 6 }}>
                                        <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Leave Year Start Month</Typography>
                                        <Select
                                            size="small" fullWidth value={form.leaveYearStartMonth}
                                            onChange={(e) => set('leaveYearStartMonth', Number(e.target.value))}
                                            sx={{ fontSize: 13 }}
                                        >
                                            {MONTHS.map((name, i) => <MenuItem key={i + 1} value={i + 1}>{name}</MenuItem>)}
                                        </Select>
                                    </Grid>
                                    <Grid size={{ xs: 12, sm: 6 }}>
                                        <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Leave Year End Date</Typography>
                                        <TextField
                                            size="small" fullWidth disabled
                                            value={fmt(endDate)}
                                            sx={{ '& .MuiInputBase-input': { fontSize: 13, bgcolor: 'action.hover', color: 'text.secondary' } }}
                                        />
                                    </Grid>
                                </Grid>

                                {/* Carryover + Entitlement */}
                                <Grid container spacing={1.5}>
                                    <Grid size={{ xs: 12, sm: 6 }}>
                                        <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Max Carryover Days</Typography>
                                        <TextField
                                            size="small" fullWidth type="number"
                                            value={form.maxCarryoverDays}
                                            onChange={(e) => set('maxCarryoverDays', Math.max(0, Number(e.target.value)))}
                                            inputProps={{ min: 0, max: 50 }}
                                            sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                        />
                                        <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.5 }}>Days above this cap expire at year end</Typography>
                                    </Grid>
                                    <Grid size={{ xs: 12, sm: 6 }}>
                                        <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Default Annual Entitlement</Typography>
                                        <TextField
                                            size="small" fullWidth type="number"
                                            value={form.defaultAnnualEntitlement}
                                            onChange={(e) => set('defaultAnnualEntitlement', Math.max(1, Number(e.target.value)))}
                                            inputProps={{ min: 1, max: 365 }}
                                            sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                        />
                                    </Grid>
                                </Grid>

                                {/* Warning days */}
                                <Grid container spacing={1.5}>
                                    <Grid size={{ xs: 12, sm: 6 }}>
                                        <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Year-End Warning (days before)</Typography>
                                        <TextField
                                            size="small" fullWidth type="number"
                                            value={form.yearEndWarningDays}
                                            onChange={(e) => set('yearEndWarningDays', Math.max(1, Number(e.target.value)))}
                                            inputProps={{ min: 1 }}
                                            sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                        />
                                    </Grid>
                                    <Grid size={{ xs: 12, sm: 6 }}>
                                        <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>Final Warning (days before)</Typography>
                                        <TextField
                                            size="small" fullWidth type="number"
                                            value={form.finalWarningDays}
                                            onChange={(e) => set('finalWarningDays', Math.max(1, Number(e.target.value)))}
                                            inputProps={{ min: 1 }}
                                            sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                        />
                                    </Grid>
                                </Grid>

                                {/* Public holidays */}
                                <Box>
                                    <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', mb: 0.75 }}>
                                        Public Holidays — Country
                                    </Typography>
                                    <Autocomplete<HolidayCountry, false, false, false>
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
                                        renderInput={(params) => (
                                            <TextField
                                                {...params}
                                                placeholder="Select a country for public holidays"
                                                sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                            />
                                        )}
                                    />
                                    <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.5 }}>
                                        Holidays are fetched from{' '}
                                        <Box component="span" sx={{ fontFamily: 'monospace' }}>date.nager.at</Box>{' '}
                                        and cached server-side. Changing country re-fetches on first request.
                                    </Typography>
                                </Box>

                                {/* Toggles */}
                                <Box sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '8px', px: 2, py: 0.5 }}>
                                    <ToggleRow title="Auto-run rollover on reset date" sub={`Resets balances automatically on ${fmt(nextReset)}`} checked={form.autoRunRollover} onChange={(v) => set('autoRunRollover', v)} />
                                    <ToggleRow title="Send year-end warning emails" sub="Notify employees with days at risk" checked={form.sendYearEndWarningEmails} onChange={(v) => set('sendYearEndWarningEmails', v)} />
                                    <ToggleRow title="Block leave spanning into next year" sub="Employees cannot submit leave beyond year end" checked={form.blockLeaveSpanningIntoNextYear} onChange={(v) => set('blockLeaveSpanningIntoNextYear', v)} />
                                    <ToggleRow title="Notify managers of team expiries" sub="CC manager on warning emails" checked={form.notifyManagersOfTeamExpiries} onChange={(v) => set('notifyManagersOfTeamExpiries', v)} />
                                </Box>

                                {mutation.isError && (
                                    <Alert severity="error">{getApiErrorMessage(mutation.error, 'Failed to save settings.')}</Alert>
                                )}
                                {showSaved && <Alert severity="success">Settings saved successfully.</Alert>}

                                <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end' }}>
                                    <Button variant="outlined" size="small" onClick={() => saved && setForm(saved)} disabled={!isDirty || mutation.isPending}
                                        sx={{ textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}>
                                        Cancel
                                    </Button>
                                    <Button variant="contained" size="small" onClick={() => mutation.mutate()} disabled={!isDirty || mutation.isPending}
                                        startIcon={mutation.isPending ? <CircularProgress size={13} color="inherit" /> : null}
                                        sx={{ textTransform: 'none', bgcolor: 'primary.main', '&:hover': { bgcolor: 'primary.dark' }, boxShadow: 'none' }}>
                                        {mutation.isPending ? 'Saving…' : 'Save Settings'}
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
                                        { bg: softBg('warning'),   color: 'warning.dark',   label: 'Carryover Cap',  value: `${form.maxCarryoverDays} days` },
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
                                    <span>On <strong>{fmt(nextReset)}</strong> the system will auto-calculate carryover (max {form.maxCarryoverDays} days), expire excess, and reset all balances.</span>
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
                                    <ScheduleRow label={`${form.yearEndWarningDays}-day warning emails`} date={fmt(warningDate)} color="warning.dark" bg={softBg('warning')} border="warning.main" badge="Scheduled" badgeBg={softBg('warning')} badgeColor="warning.dark" />
                                    <ScheduleRow label={`${form.finalWarningDays}-day final warning`} date={fmt(finalWarnDate)} color="warning.dark" bg={softBg('warning')} border="warning.main" badge="Scheduled" badgeBg={softBg('warning')} badgeColor="warning.dark" />
                                    <ScheduleRow label="Year-end rollover" date={`${fmt(nextReset)} · midnight`} color="error.dark" bg={softBg('error')} border="error.main" badge="Year End" badgeBg={softBg('error')} badgeColor="error.dark" />
                                    <ScheduleRow label="New year opens" date={fmt(nextReset)} color="success.dark" bg={softBg('success')} border="success.main" badge="New Year" badgeBg={softBg('success')} badgeColor="success.dark" />
                                    <Button variant="outlined" fullWidth size="small" sx={{ mt: 0.5, textTransform: 'none', borderColor: 'divider', color: 'text.secondary', fontSize: 12 }}>
                                        ▶ Run Rollover Manually
                                    </Button>
                                </Stack>
                            </Box>
                        </Box>
                    </Stack>
                </Grid>
            </Grid>

            {/* ── Carryover Preview ─────────────────────────────────────────── */}
            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                        Carryover Preview — End of {yearLabel}
                    </Typography>
                    <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>{form.maxCarryoverDays}-day max cap</Typography>
                </Box>

                {/* Example cards */}
                <Box sx={{ p: 2.25, pb: 0 }}>
                    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: '14px', mb: 2 }}>
                        {([
                            { bg: softBg('success'), border: 'success.main', color: 'success.dark', title: `Under cap (< ${form.maxCarryoverDays} days unused)`, icon: '✅', body: 'All days carry over', sub: `New balance = unused + ${form.defaultAnnualEntitlement}` },
                            { bg: softBg('warning'), border: 'warning.main', color: 'warning.dark', title: `At cap (= ${form.maxCarryoverDays} days unused)`, icon: '✅', body: `${form.maxCarryoverDays} days carry (cap hit)`, sub: `New balance = ${form.maxCarryoverDays} + ${form.defaultAnnualEntitlement} = ${form.maxCarryoverDays + form.defaultAnnualEntitlement}` },
                            { bg: softBg('error'),   border: 'error.main',   color: 'error.dark',   title: `Over cap (> ${form.maxCarryoverDays} days unused)`, icon: '⚠️', body: `${form.maxCarryoverDays} carry · excess expires`, sub: `New balance = ${form.maxCarryoverDays} + ${form.defaultAnnualEntitlement} = ${form.maxCarryoverDays + form.defaultAnnualEntitlement}` },
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
