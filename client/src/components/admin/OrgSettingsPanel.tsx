import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Grid from '@mui/material/Grid'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import TextField from '@mui/material/TextField'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Typography from '@mui/material/Typography'
import { getAppSettings, resetReminders, updateAppSettings } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { AppSettings, ReminderFrequency, ReminderSetting } from '../../lib/types'
import { softBg, type SxColor } from '../../lib/theme-tokens'
import { SweetAlert } from '../ui'

// ── Static reminder catalogue (display metadata; configurable state comes from
// the backend, keyed by the same id) ─────────────────────────────────────────
type ReminderMeta = { id: string; emoji: string; name: string; desc: string; tail: string }

const REMINDERS_META: ReminderMeta[] = [
    { id: 'pending-approvals', emoji: '⏳', name: 'Pending Approvals', desc: 'Get notified when leave or timesheets are awaiting review.', tail: "you'll receive a summary of pending leave requests and timesheets." },
    { id: 'late-submissions', emoji: '📋', name: 'Late Timesheet Submissions', desc: 'Remind team members to submit their timesheets.', tail: "team members who haven't submitted their timesheet will be reminded." },
    { id: 'team-alerts', emoji: '👥', name: 'Team Alerts', desc: 'Notifications about absences, conflicts, or team issues.', tail: "you'll see alerts for team members not checked in, conflicts, or other issues." },
    { id: 'low-balance', emoji: '🔔', name: 'Low Leave Balance', desc: 'Alert employees when their leave balance is running low.', tail: 'employees with fewer than 5 days remaining will be notified.' },
    { id: 'department-digest', emoji: '📊', name: 'Department Digest', desc: 'Weekly summary of department metrics and trends.', tail: "department managers receive a digest of their team's metrics." },
    { id: 'birthday-reminder', emoji: '🎂', name: 'Birthday Reminders', desc: 'Notify the team about upcoming employee birthdays.', tail: "you'll see any team members with birthdays that week." },
    { id: 'check-in', emoji: '🟢', name: 'Check-In Reminder', desc: 'Remind employees to check in at the start of their workday.', tail: "employees who haven't checked in will be reminded to do so." },
    { id: 'check-out', emoji: '🔴', name: 'Check-Out Reminder', desc: 'Remind employees to check out at the end of their workday.', tail: 'employees still checked in will be reminded to check out and complete their timesheet.' },
]
const META_BY_ID = new Map(REMINDERS_META.map((m) => [m.id, m]))

function formatTime(hhmm: string): string {
    const [h, m] = (hhmm ?? '').split(':').map(Number)
    if (Number.isNaN(h) || Number.isNaN(m)) return hhmm
    const period = h < 12 ? 'AM' : 'PM'
    const hour12 = h % 12 === 0 ? 12 : h % 12
    return `${hour12}:${String(m).padStart(2, '0')} ${period}`
}

function reminderPreview(r: ReminderSetting): { when: string; rest: string } {
    const meta = META_BY_ID.get(r.id)
    const when = r.frequency === 'daily' ? `Every day at ${formatTime(r.time)}` : `Once a week at ${formatTime(r.time)}`
    return { when, rest: `, ${meta?.tail ?? 'the reminder will be sent.'}` }
}

// ── Card chrome (mirrors AppSettingsPanel) ───────────────────────────────────
function Card({ title, icon, sub, head, headBg, borderColor, children }: {
    title: string; icon: string; sub?: string; head?: React.ReactNode
    headBg?: SxColor; borderColor?: SxColor; children: React.ReactNode
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: borderColor ?? 'divider', borderRadius: '10px', overflow: 'hidden' }}>
            <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', bgcolor: headBg ?? 'action.hover', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <Box>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', display: 'flex', alignItems: 'center', gap: 1 }}>
                        <Box component="span" sx={{ fontSize: 16 }}>{icon}</Box>{title}
                    </Typography>
                    {sub && <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.25 }}>{sub}</Typography>}
                </Box>
                {head}
            </Box>
            <Box sx={{ p: 2.25 }}>{children}</Box>
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

export default function OrgSettingsPanel() {
    const queryClient = useQueryClient()
    const { data: saved, isLoading, isError, error } = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings })

    const [form, setForm] = useState<AppSettings | null>(null)
    const [showSaved, setShowSaved] = useState(false)

    // Sync the loaded settings into editable form state. Adjusted during render
    // (not an effect) per
    // https://react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes
    const [prevSaved, setPrevSaved] = useState(saved)
    if (saved !== prevSaved) {
        setPrevSaved(saved)
        if (saved) setForm(saved)
    }

    const mutation = useMutation({
        mutationFn: (data: AppSettings) => updateAppSettings(data),
        onSuccess: (data) => {
            queryClient.setQueryData(['appSettings'], data)
            setForm(data)
            setShowSaved(true)
            setTimeout(() => setShowSaved(false), 3000)
        },
    })

    const resetRemindersMutation = useMutation({
        mutationFn: resetReminders,
        onSuccess: (data) => {
            queryClient.setQueryData(['appSettings'], data)
            setForm(data)
            void SweetAlert.fire({ icon: 'success', title: 'Reminders reset', text: 'All reminders restored to factory defaults.', timer: 2200, showConfirmButton: false })
        },
        onError: (err) => SweetAlert.fire({ icon: 'error', title: 'Failed', text: getApiErrorMessage(err, 'Could not reset reminders.') }),
    })

    const isDirty = useMemo(() => JSON.stringify(form) !== JSON.stringify(saved), [form, saved])
    const enabledCount = form?.reminders.filter((r) => r.enabled).length ?? 0

    if (isLoading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    }
    if (isError || !form) {
        return <Box sx={{ p: 2 }}><Alert severity="error">{getApiErrorMessage(error, 'Failed to load settings.')}</Alert></Box>
    }

    const set = <K extends keyof AppSettings>(key: K, val: AppSettings[K]) => setForm((f) => (f ? { ...f, [key]: val } : f))

    const setReminder = (id: string, patch: Partial<ReminderSetting>) =>
        setForm((f) => (f ? { ...f, reminders: f.reminders.map((r) => (r.id === id ? { ...r, ...patch } : r)) } : f))

    const onResetReminders = async () => {
        const res = await SweetAlert.fire({
            title: 'Reset all reminders?',
            text: 'Restore every reminder to factory defaults. Your custom times and toggles will be lost.',
            icon: 'warning', showCancelButton: true, confirmButtonText: 'Yes, reset',
            cancelButtonText: 'Cancel', confirmButtonColor: '#EF4444', reverseButtons: true,
        })
        if (res.isConfirmed) resetRemindersMutation.mutate()
    }

    const onConnectSlack = () => SweetAlert.fire({
        icon: 'info', title: 'Slack is configured server-side',
        html: 'Set the incoming-webhook URL in the server configuration (<code>Slack:WebhookUrl</code>) to connect a workspace. Once set, enable “Send to Slack” here to route reminders to your channel.',
    })

    return (
        <Stack spacing={2}>
            {/* Header */}
            <Box>
                <Typography sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary' }}>🔔 Notification Settings</Typography>
                <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>Configure reminders and notifications for your organization</Typography>
            </Box>

            {showSaved && <Alert severity="success">Settings saved successfully.</Alert>}
            {mutation.isError && <Alert severity="error">{getApiErrorMessage(mutation.error, 'Failed to save settings.')}</Alert>}

            {/* Reminders */}
            <Card title="Reminders" icon="🔔" sub={`${enabledCount} of ${form.reminders.length} enabled`}>
                {form.reminders.map((r) => {
                    const meta = META_BY_ID.get(r.id)
                    const preview = reminderPreview(r)
                    return (
                        <Box key={r.id} sx={{ borderBottom: '1px solid', borderColor: 'divider', '&:last-of-type': { borderBottom: 'none' }, py: 1.5 }}>
                            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 2 }}>
                                <Box sx={{ flex: 1 }}>
                                    <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                                        <Box component="span" sx={{ mr: 0.75 }}>{meta?.emoji ?? '🔔'}</Box>{meta?.name ?? r.id}
                                    </Typography>
                                    <Typography sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.4 }}>{meta?.desc}</Typography>
                                </Box>
                                <Switch checked={r.enabled} onChange={(e) => setReminder(r.id, { enabled: e.target.checked })} size="small" />
                            </Box>

                            {r.enabled && (
                                <Box sx={{ mt: 1.5 }}>
                                    <Grid container spacing={1.5}>
                                        <Grid size={{ xs: 12, sm: 6 }}>
                                            <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.04em', mb: 0.5 }}>Time</Typography>
                                            <TextField
                                                type="time" size="small" fullWidth value={r.time}
                                                onChange={(e) => setReminder(r.id, { time: e.target.value })}
                                                sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                                            />
                                        </Grid>
                                        <Grid size={{ xs: 12, sm: 6 }}>
                                            <Typography sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.04em', mb: 0.5 }}>Frequency</Typography>
                                            <ToggleButtonGroup
                                                exclusive size="small" value={r.frequency}
                                                onChange={(_, v: ReminderFrequency | null) => v && setReminder(r.id, { frequency: v })}
                                            >
                                                <ToggleButton value="daily" sx={{ textTransform: 'none', fontSize: 12, px: 2 }}>Daily</ToggleButton>
                                                <ToggleButton value="weekly" sx={{ textTransform: 'none', fontSize: 12, px: 2 }}>Weekly</ToggleButton>
                                            </ToggleButtonGroup>
                                        </Grid>
                                    </Grid>
                                    <Box sx={{ mt: 1.5, p: '10px 14px', bgcolor: 'action.hover', border: '1px solid', borderColor: 'divider', borderRadius: '8px', fontSize: 12, color: 'text.secondary', lineHeight: 1.5 }}>
                                        <Box component="span" sx={{ fontWeight: 600, color: 'text.primary' }}>{preview.when}</Box>{preview.rest}
                                    </Box>
                                </Box>
                            )}
                        </Box>
                    )
                })}
            </Card>

            <Grid container spacing={2}>
                {/* Email notifications */}
                <Grid size={{ xs: 12, md: 6 }}>
                    <Card title="Email Notifications" icon="📧">
                        <SettingRow label="Notification emails" desc="Send reminders and alerts via email"
                            control={<Switch size="small" checked={form.emailNotificationsEnabled} onChange={(e) => set('emailNotificationsEnabled', e.target.checked)} />} />
                        <SettingRow label="Daily digest" desc="Single email per day with all notifications"
                            control={<Switch size="small" checked={form.emailDailyDigest} onChange={(e) => set('emailDailyDigest', e.target.checked)} />} />
                        <SettingRow label="Urgent alerts only" desc="Only send critical issues, not routine reminders"
                            control={<Switch size="small" checked={form.emailUrgentOnly} onChange={(e) => set('emailUrgentOnly', e.target.checked)} />} />
                    </Card>
                </Grid>

                {/* Slack */}
                <Grid size={{ xs: 12, md: 6 }}>
                    <Card title="Slack Integration" icon="💬">
                        <SettingRow label="Send to Slack" desc="Post reminders to your configured Slack channel"
                            control={<Switch size="small" checked={form.slackEnabled} disabled={!form.slackConnected} onChange={(e) => set('slackEnabled', e.target.checked)} />} />
                        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', pt: 1.5 }}>
                            <Box component="span" sx={{
                                display: 'inline-flex', alignItems: 'center', gap: 0.75, px: 1.25, py: 0.4, borderRadius: '20px', fontSize: 11, fontWeight: 600,
                                bgcolor: form.slackConnected ? softBg('success') : 'action.hover',
                                color: form.slackConnected ? 'success.dark' : 'text.secondary',
                            }}>
                                <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: form.slackConnected ? 'success.main' : 'text.disabled' }} />
                                {form.slackConnected ? 'Connected' : 'Not connected'}
                            </Box>
                        </Box>
                        {!form.slackConnected && (
                            <Button onClick={onConnectSlack} variant="outlined" size="small" fullWidth
                                sx={{ mt: 1.5, textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}>
                                🔗 Connect Slack Workspace
                            </Button>
                        )}
                    </Card>
                </Grid>
            </Grid>

            {/* Reset lives with the reminders it resets, and is a preference reset — the
                irreversible data deletion that used to sit beside it under a "Danger
                Zone" heading is now on Administration › Data Maintenance. */}
            <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end', alignItems: 'center' }}>
                <Button variant="outlined" size="small" onClick={onResetReminders} disabled={resetRemindersMutation.isPending}
                    startIcon={resetRemindersMutation.isPending ? <CircularProgress size={13} color="inherit" /> : null}
                    sx={{ textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}>
                    ↻ Reset reminders to defaults
                </Button>
                <Button variant="contained" size="small" onClick={() => form && mutation.mutate(form)} disabled={!isDirty || mutation.isPending}
                    startIcon={mutation.isPending ? <CircularProgress size={13} color="inherit" /> : null}
                    sx={{ textTransform: 'none', boxShadow: 'none' }}>
                    {mutation.isPending ? 'Saving…' : '💾 Save changes'}
                </Button>
            </Box>
        </Stack>
    )
}
