import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import FormControlLabel from '@mui/material/FormControlLabel'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import TextField from '@mui/material/TextField'
import {
    SweetAlert,
    AppDialog,
    AppDialogTitle,
    AppDialogContent,
    AppDialogActions,
    cancelBtnSx,
    saveBtnSx,
} from '../ui'
import {
    createProjectActivityType,
    deleteProjectActivityType,
    getProjectActivityTypes,
    updateProjectActivityType,
    type UpsertProjectActivityTypeRequest,
} from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'
import type { ProjectActivityType } from '../../lib/types'

/* ─── tokens ─────────────────────────────────────────────────────────────── */

const HEADER_GRADIENTS: Record<string, string> = {
    blue:    'linear-gradient(135deg, #DBEAFE 0%, #BFDBFE 100%)',
    green:   'linear-gradient(135deg, #DCFCE7 0%, #BBF7D0 100%)',
    pink:    'linear-gradient(135deg, #FCE7F3 0%, #FBCFE8 100%)',
    amber:   'linear-gradient(135deg, #FEF3C7 0%, #FDE68A 100%)',
    purple:  'linear-gradient(135deg, #EDE9FE 0%, #DDD6FE 100%)',
    red:     'linear-gradient(135deg, #FEE2E2 0%, #FECACA 100%)',
    orange:  'linear-gradient(135deg, #FFEDD5 0%, #FED7AA 100%)',
    cyan:    'linear-gradient(135deg, #CFFAFE 0%, #A5F3FC 100%)',
    default: 'linear-gradient(135deg, #F1F5F9 0%, #E2E8F0 100%)',
}

const COLOR_KEYS = ['blue', 'green', 'pink', 'amber', 'purple', 'red', 'orange', 'cyan', 'default']

type StatusFilter = 'all' | 'enabled' | 'disabled'

function getErrorMessage(error: unknown) {
    return getApiErrorMessage(error, 'Something went wrong. Please try again.')
}

function gradientFor(colorKey: string) {
    return HEADER_GRADIENTS[colorKey] ?? HEADER_GRADIENTS.default
}

/* ════════════════════════════════════════════════════════════════════════ */

function ActivityTypesPanel() {
    const queryClient = useQueryClient()

    const [createOpen, setCreateOpen] = useState(false)
    const [editType, setEditType] = useState<ProjectActivityType | null>(null)
    const [searchText, setSearchText] = useState('')
    const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')

    const { data: activityTypes = [], isLoading, isError, error } = useQuery({
        queryKey: ['projectActivityTypes'],
        queryFn: getProjectActivityTypes,
    })

    const filtered = useMemo(() => {
        let out = activityTypes
        if (statusFilter === 'enabled') out = out.filter((a) => a.isActive)
        else if (statusFilter === 'disabled') out = out.filter((a) => !a.isActive)

        if (searchText.trim()) {
            const q = searchText.trim().toLowerCase()
            out = out.filter((a) =>
                a.name.toLowerCase().includes(q) ||
                a.description.toLowerCase().includes(q)
            )
        }
        return [...out].sort((a, b) => a.name.localeCompare(b.name))
    }, [activityTypes, statusFilter, searchText])

    /* Aggregate stats */
    const totalActive = activityTypes.filter((a) => a.isActive).length
    const totalHoursYTD = activityTypes.reduce((s, a) => s + a.hoursYtd, 0)
    const totalProjects = activityTypes.reduce((s, a) => Math.max(s, a.usedInProjects), 0)
    const mostUsed = [...activityTypes]
        .filter((a) => a.isActive)
        .sort((a, b) => b.hoursYtd - a.hoursYtd)[0]

    /* Mutations */
    const createMutation = useMutation({
        mutationFn: createProjectActivityType,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['projectActivityTypes'] })
            setCreateOpen(false)
        },
    })
    const updateMutation = useMutation({
        mutationFn: ({ id, payload }: { id: number; payload: UpsertProjectActivityTypeRequest }) =>
            updateProjectActivityType(id, payload),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['projectActivityTypes'] })
            setEditType(null)
        },
    })
    const deleteMutation = useMutation({
        mutationFn: deleteProjectActivityType,
        onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['projectActivityTypes'] }),
    })

    const toggleActive = (a: ProjectActivityType) => {
        const payload: UpsertProjectActivityTypeRequest = {
            name: a.name,
            description: a.description,
            icon: a.icon,
            colorKey: a.colorKey,
            isActive: !a.isActive,
        }
        updateMutation.mutate({ id: a.id, payload })
    }

    if (isLoading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    }
    if (isError) {
        return <Box sx={{ p: 2 }}><Alert severity="error">{getErrorMessage(error)}</Alert></Box>
    }

    return (
        <Box>
            {deleteMutation.isError && (
                <Alert severity="error" sx={{ mb: 2 }}>{getErrorMessage(deleteMutation.error)}</Alert>
            )}

            {/* Stats row */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <StatCard
                    label="🏷️ Activity Types"
                    value={String(totalActive)}
                    sub={`of ${activityTypes.length} configured · ${activityTypes.length - totalActive} disabled`}
                />
                <StatCard
                    label="⏱ Hours Logged YTD"
                    value={totalHoursYTD.toLocaleString()}
                    valueColor={'primary.main'}
                    sub="across all activity types"
                />
                <StatCard
                    label="📊 Projects"
                    value={String(totalProjects)}
                    valueColor={'success.main'}
                    sub="tracking activities"
                />
                <StatCard
                    label="📈 Most Used"
                    value={mostUsed && totalHoursYTD > 0 ? `${mostUsed.icon} ${mostUsed.name}` : '—'}
                    valueSize={18}
                    sub={mostUsed && totalHoursYTD > 0 ? `${mostUsed.hoursYtd} hours` : 'no time logged yet'}
                />
            </Box>

            {/* Toolbar */}
            <Box sx={{
                bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                p: '10px 12px', display: 'flex', gap: '10px', flexWrap: 'wrap',
                alignItems: 'center', mb: '14px',
            }}>
                <Box sx={{ flex: 1, minWidth: 200, maxWidth: 320 }}>
                    <Box
                        component="input"
                        type="search"
                        placeholder="Search activity types…"
                        value={searchText}
                        onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearchText(e.target.value)}
                        sx={{
                            width: '100%', p: '7px 10px', fontSize: 13, fontFamily: 'inherit',
                            border: '1px solid', borderColor: 'divider', borderRadius: '6px', outline: 'none',
                            bgcolor: 'background.paper', color: 'text.primary',
                            '&::placeholder': { color: 'text.disabled' },
                            '&:focus': { borderColor: 'primary.main' },
                        }}
                    />
                </Box>
                <SelectFilter
                    value={statusFilter}
                    onChange={(v) => setStatusFilter(v as StatusFilter)}
                    options={[
                        { value: 'all', label: `All statuses (${activityTypes.length})` },
                        { value: 'enabled', label: `Enabled (${totalActive})` },
                        { value: 'disabled', label: `Disabled (${activityTypes.length - totalActive})` },
                    ]}
                />
                <Box sx={{ flex: 1 }} />
                <Box
                    component="button"
                    onClick={() => setCreateOpen(true)}
                    sx={{
                        bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '14px', py: '7px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit', whiteSpace: 'nowrap',
                        '&:hover': { bgcolor: 'primary.dark' },
                    }}
                >
                    + New activity type
                </Box>
            </Box>

            {/* Cards grid */}
            {filtered.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    No activity types match the current filters.
                </Box>
            ) : (
                <Box sx={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))',
                    gap: '14px',
                }}>
                    {filtered.map((a) => (
                        <ActivityTypeCard
                            key={a.id}
                            type={a}
                            onEdit={() => setEditType(a)}
                            onToggle={() => toggleActive(a)}
                            onDelete={async () => {
                                const result = await SweetAlert.fire({
                                    title: `Delete "${a.name}"?`,
                                    text: 'This activity type will be removed.',
                                    icon: 'warning',
                                    showCancelButton: true,
                                    confirmButtonText: 'Yes, delete',
                                    cancelButtonText: 'Cancel',
                                    confirmButtonColor: '#EF4444',
                                    reverseButtons: true,
                                })
                                if (result.isConfirmed) deleteMutation.mutate(a.id)
                            }}
                        />
                    ))}
                    <AddCard onClick={() => setCreateOpen(true)} />
                </Box>
            )}

            <ActivityTypeFormDialog
                key={createOpen ? 'at-create-open' : 'at-create-closed'}
                open={createOpen}
                title="New Activity Type"
                isPending={createMutation.isPending}
                error={createMutation.error}
                onClose={() => setCreateOpen(false)}
                onSubmit={(payload) => createMutation.mutate(payload)}
            />

            <ActivityTypeFormDialog
                key={editType ? `at-edit-${editType.id}` : 'at-edit-none'}
                open={!!editType}
                title="Edit Activity Type"
                initial={editType ?? undefined}
                isPending={updateMutation.isPending}
                error={updateMutation.error}
                onClose={() => setEditType(null)}
                onSubmit={(payload) => editType && updateMutation.mutate({ id: editType.id, payload })}
            />
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Card                                                                     */
/* ════════════════════════════════════════════════════════════════════════ */

function ActivityTypeCard({ type: a, onEdit, onToggle, onDelete }: {
    type: ProjectActivityType
    onEdit: () => void
    onToggle: () => void
    onDelete: () => void
}) {
    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px',
            overflow: 'hidden', transition: 'all 0.15s',
            display: 'flex', flexDirection: 'column',
            opacity: a.isActive ? 1 : 0.65,
            '&:hover': { transform: 'translateY(-2px)', boxShadow: '0 6px 20px rgba(0,0,0,0.06)' },
        }}>
            {/* Header */}
            <Box sx={{
                p: '18px 20px', position: 'relative', overflow: 'hidden',
                borderBottom: '1px solid #F3F4F6',
                background: a.isActive ? gradientFor(a.colorKey) : '#E5E7EB',
            }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <Box sx={{ minWidth: 0 }}>
                        <Box sx={{
                            fontSize: 32, lineHeight: 1, mb: '10px',
                            filter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.1))',
                        }}>
                            {a.icon}
                        </Box>
                        <Box sx={{ fontSize: 16, fontWeight: 700, color: 'text.primary', lineHeight: 1.2, mb: '4px' }}>
                            {a.name}
                        </Box>
                        {a.description && (
                            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5, maxWidth: '95%' }}>
                                {a.description}
                            </Box>
                        )}
                    </Box>
                    <Box sx={{ display: 'flex', gap: '4px' }}>
                        <HeaderIconBtn title="Edit" onClick={onEdit}>✏️</HeaderIconBtn>
                        <HeaderIconBtn title="Delete" onClick={onDelete}>🗑</HeaderIconBtn>
                    </Box>
                </Box>
            </Box>

            {/* Toggle row */}
            <Box sx={{
                display: 'flex', alignItems: 'center', gap: '8px',
                p: '10px 20px', bgcolor: 'action.hover', borderBottom: '1px solid #F3F4F6',
            }}>
                <Switch
                    size="small"
                    checked={a.isActive}
                    onChange={onToggle}
                    sx={{
                        '& .MuiSwitch-switchBase.Mui-checked': { color: 'success.main' },
                        '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: 'success.main' },
                    }}
                />
                <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary' }}>
                    {a.isActive ? 'Enabled' : 'Disabled'}
                </Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
                    · {a.isActive ? 'available for time tracking' : 'hidden from timesheets'}
                </Box>
            </Box>

            {/* Usage */}
            <Box sx={{ p: '16px 20px', mt: 'auto' }}>
                <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.6 }}>
                    <Box component="strong" sx={{ color: 'text.primary', fontWeight: 700, fontSize: 13 }}>
                        {a.hoursYtd.toLocaleString()}
                    </Box>{' '}
                    hours logged across{' '}
                    <Box component="strong" sx={{ color: 'text.primary', fontWeight: 700, fontSize: 13 }}>
                        {a.usedInProjects}
                    </Box>{' '}
                    project{a.usedInProjects === 1 ? '' : 's'}
                </Box>
            </Box>
        </Box>
    )
}

function HeaderIconBtn({ title, onClick, children }: {
    title: string
    onClick: () => void
    children: React.ReactNode
}) {
    return (
        <Box
            component="button"
            title={title}
            onClick={(e: React.MouseEvent) => { e.stopPropagation(); onClick() }}
            sx={{
                width: 28, height: 28, borderRadius: '6px',
                bgcolor: 'rgba(255,255,255,0.6)', border: 'none',
                cursor: 'pointer', fontFamily: 'inherit',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 12, lineHeight: 1,
                backdropFilter: 'blur(4px)',
                '&:hover': { bgcolor: 'rgba(255,255,255,0.9)' },
            }}
        >
            {children}
        </Box>
    )
}

function AddCard({ onClick }: { onClick: () => void }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                bgcolor: 'action.hover', border: `2px dashed #D1D5DB`,
                borderRadius: '12px', p: '40px 20px', minHeight: 240,
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                cursor: 'pointer', fontFamily: 'inherit', textAlign: 'center',
                color: 'text.secondary', transition: 'all 0.15s',
                '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary'), transform: 'translateY(-2px)' },
            }}
        >
            <Box sx={{
                width: 56, height: 56, borderRadius: '50%',
                bgcolor: 'background.paper', border: '2px dashed #D1D5DB',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 24, color: 'text.secondary', mb: '12px',
            }}>+</Box>
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>Create new activity type</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5 }}>
                Define a new work category<br/>for time tracking on projects
            </Box>
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Small UI bits                                                             */
/* ════════════════════════════════════════════════════════════════════════ */

function StatCard({ label, value, sub, valueColor, valueSize = 26 }: {
    label: string
    value: string
    sub: string
    valueColor?: string
    valueSize?: number
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '14px 16px' }}>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: valueSize, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '6px' }}>{sub}</Box>
        </Box>
    )
}

function SelectFilter({ value, onChange, options }: {
    value: string
    onChange: (v: string) => void
    options: { value: string; label: string }[]
}) {
    return (
        <Box
            component="select"
            value={value}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => onChange(e.target.value)}
            sx={{
                fontSize: 12, fontFamily: 'inherit', p: '7px 10px',
                border: '1px solid', borderColor: 'divider', borderRadius: '6px',
                color: 'text.primary', bgcolor: 'background.paper', outline: 'none', cursor: 'pointer',
                '&:focus': { borderColor: 'primary.main' },
            }}
        >
            {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Form dialog                                                               */
/* ════════════════════════════════════════════════════════════════════════ */

function ActivityTypeFormDialog(props: {
    open: boolean
    title: string
    initial?: ProjectActivityType
    isPending: boolean
    error: Error | null
    onClose: () => void
    onSubmit: (payload: UpsertProjectActivityTypeRequest) => void
}) {
    const i = props.initial
    const [name, setName] = useState(i?.name ?? '')
    const [icon, setIcon] = useState(i?.icon ?? '🏷️')
    const [colorKey, setColorKey] = useState<string>(i?.colorKey ?? 'default')
    const [description, setDescription] = useState(i?.description ?? '')
    const [isActive, setIsActive] = useState(i?.isActive ?? true)

    useEffect(() => {
        if (!props.open) return
        const x = props.initial
        setName(x?.name ?? '')
        setIcon(x?.icon ?? '🏷️')
        setColorKey(x?.colorKey ?? 'default')
        setDescription(x?.description ?? '')
        setIsActive(x?.isActive ?? true)
    }, [props.open, props.initial])

    const submit = () => {
        props.onSubmit({
            name: name.trim(),
            description: description.trim(),
            icon: icon.trim() || '🏷️',
            colorKey,
            isActive,
        })
    }

    return (
        <AppDialog open={props.open} onClose={props.onClose} maxWidth="sm">
            <AppDialogTitle>{props.title}</AppDialogTitle>
            <AppDialogContent>
                <Stack spacing={2}>
                    <Stack direction="row" spacing={2}>
                        <TextField
                            label="Icon"
                            value={icon}
                            onChange={(e) => setIcon(e.target.value)}
                            sx={{ width: 90 }}
                            inputProps={{ maxLength: 8 }}
                            helperText="emoji"
                        />
                        <TextField
                            label="Name"
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            fullWidth
                            required
                            inputProps={{ maxLength: 100 }}
                        />
                    </Stack>

                    <TextField
                        label="Description"
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        fullWidth
                        multiline
                        minRows={2}
                        inputProps={{ maxLength: 300 }}
                    />

                    <TextField
                        select
                        label="Color theme"
                        value={colorKey}
                        onChange={(e) => setColorKey(e.target.value)}
                        fullWidth
                    >
                        {COLOR_KEYS.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
                    </TextField>

                    <FormControlLabel
                        control={<Switch checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />}
                        label="Active"
                    />

                    {props.error != null && (
                        <Alert severity="error">{getErrorMessage(props.error)}</Alert>
                    )}
                </Stack>
            </AppDialogContent>
            <AppDialogActions>
                <Button variant="outlined" sx={cancelBtnSx} onClick={props.onClose} disabled={props.isPending}>Cancel</Button>
                <Button
                    variant="contained"
                    sx={saveBtnSx}
                    disabled={props.isPending || !name.trim()}
                    onClick={submit}
                >
                    Save
                </Button>
            </AppDialogActions>
        </AppDialog>
    )
}

export default ActivityTypesPanel
