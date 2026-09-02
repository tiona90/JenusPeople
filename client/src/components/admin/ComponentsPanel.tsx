import { useMemo, useState } from 'react'
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
    createProjectComponent,
    deleteProjectComponent,
    getProjectComponents,
    updateProjectComponent,
    type UpsertProjectComponentRequest,
} from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'
import type { ProjectComponent } from '../../lib/types'

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

const DEFAULT_ICON = '🧩'

type StatusFilter = 'all' | 'enabled' | 'disabled'

function getErrorMessage(error: unknown) {
    return getApiErrorMessage(error, 'Something went wrong. Please try again.')
}

function gradientFor(colorKey: string) {
    return HEADER_GRADIENTS[colorKey] ?? HEADER_GRADIENTS.default
}

/* ════════════════════════════════════════════════════════════════════════ */

function ComponentsPanel() {
    const queryClient = useQueryClient()

    const [createOpen, setCreateOpen] = useState(false)
    const [editComponent, setEditComponent] = useState<ProjectComponent | null>(null)
    const [searchText, setSearchText] = useState('')
    const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')

    const { data: components = [], isLoading, isError, error } = useQuery({
        queryKey: ['projectComponents'],
        queryFn: getProjectComponents,
    })

    const filtered = useMemo(() => {
        let out = components
        if (statusFilter === 'enabled') out = out.filter((c) => c.isActive)
        else if (statusFilter === 'disabled') out = out.filter((c) => !c.isActive)

        if (searchText.trim()) {
            const q = searchText.trim().toLowerCase()
            out = out.filter((c) =>
                c.name.toLowerCase().includes(q) ||
                c.description.toLowerCase().includes(q)
            )
        }
        return [...out].sort((a, b) => a.name.localeCompare(b.name))
    }, [components, statusFilter, searchText])

    const totalActive = components.filter((c) => c.isActive).length

    /* Mutations */
    const createMutation = useMutation({
        mutationFn: createProjectComponent,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['projectComponents'] })
            setCreateOpen(false)
        },
    })
    const updateMutation = useMutation({
        mutationFn: ({ id, payload }: { id: number; payload: UpsertProjectComponentRequest }) =>
            updateProjectComponent(id, payload),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['projectComponents'] })
            setEditComponent(null)
        },
    })
    const deleteMutation = useMutation({
        mutationFn: deleteProjectComponent,
        onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['projectComponents'] }),
    })

    // The endpoint upserts rather than patches, so the switch has to send the
    // whole component back with only IsActive flipped.
    const toggleActive = (c: ProjectComponent) => {
        const payload: UpsertProjectComponentRequest = {
            name: c.name,
            description: c.description,
            icon: c.icon,
            colorKey: c.colorKey,
            isActive: !c.isActive,
        }
        updateMutation.mutate({ id: c.id, payload })
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

            {/* Stats row — one card only: nothing logs against a component yet, so
                there are no hours or project counts to report. */}
            <Box sx={{ display: 'flex', mb: '14px' }}>
                <Box sx={{ width: { xs: '100%', sm: 280 } }}>
                    <StatCard
                        label="🧩 Components"
                        value={String(totalActive)}
                        sub={`of ${components.length} configured · ${components.length - totalActive} disabled`}
                    />
                </Box>
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
                        placeholder="Search components…"
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
                        { value: 'all', label: `All statuses (${components.length})` },
                        { value: 'enabled', label: `Enabled (${totalActive})` },
                        { value: 'disabled', label: `Disabled (${components.length - totalActive})` },
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
                    + New component
                </Box>
            </Box>

            {/* Cards grid */}
            {filtered.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    No components match the current filters.
                </Box>
            ) : (
                <Box sx={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))',
                    gap: '14px',
                }}>
                    {filtered.map((c) => (
                        <ComponentCard
                            key={c.id}
                            component={c}
                            onEdit={() => setEditComponent(c)}
                            onToggle={() => toggleActive(c)}
                            onDelete={async () => {
                                const result = await SweetAlert.fire({
                                    title: `Delete "${c.name}"?`,
                                    text: 'This component will be removed.',
                                    icon: 'warning',
                                    showCancelButton: true,
                                    confirmButtonText: 'Yes, delete',
                                    cancelButtonText: 'Cancel',
                                    confirmButtonColor: '#EF4444',
                                    reverseButtons: true,
                                })
                                if (result.isConfirmed) deleteMutation.mutate(c.id)
                            }}
                        />
                    ))}
                    <AddCard onClick={() => setCreateOpen(true)} />
                </Box>
            )}

            <ComponentFormDialog
                key={createOpen ? 'pc-create-open' : 'pc-create-closed'}
                open={createOpen}
                title="New Component"
                isPending={createMutation.isPending}
                error={createMutation.error}
                onClose={() => setCreateOpen(false)}
                onSubmit={(payload) => createMutation.mutate(payload)}
            />

            <ComponentFormDialog
                key={editComponent ? `pc-edit-${editComponent.id}` : 'pc-edit-none'}
                open={!!editComponent}
                title="Edit Component"
                initial={editComponent ?? undefined}
                isPending={updateMutation.isPending}
                error={updateMutation.error}
                onClose={() => setEditComponent(null)}
                onSubmit={(payload) => editComponent && updateMutation.mutate({ id: editComponent.id, payload })}
            />
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Card                                                                     */
/* ════════════════════════════════════════════════════════════════════════ */

function ComponentCard({ component: c, onEdit, onToggle, onDelete }: {
    component: ProjectComponent
    onEdit: () => void
    onToggle: () => void
    onDelete: () => void
}) {
    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px',
            overflow: 'hidden', transition: 'all 0.15s',
            display: 'flex', flexDirection: 'column',
            opacity: c.isActive ? 1 : 0.65,
            '&:hover': { transform: 'translateY(-2px)', boxShadow: '0 6px 20px rgba(0,0,0,0.06)' },
        }}>
            {/* Header */}
            <Box sx={{
                p: '18px 20px', position: 'relative', overflow: 'hidden',
                borderBottom: '1px solid #F3F4F6',
                background: c.isActive ? gradientFor(c.colorKey) : '#E5E7EB',
            }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <Box sx={{ minWidth: 0 }}>
                        <Box sx={{
                            fontSize: 32, lineHeight: 1, mb: '10px',
                            filter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.1))',
                        }}>
                            {c.icon}
                        </Box>
                        <Box sx={{ fontSize: 16, fontWeight: 700, color: 'text.primary', lineHeight: 1.2, mb: '4px' }}>
                            {c.name}
                        </Box>
                        {c.description && (
                            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5, maxWidth: '95%' }}>
                                {c.description}
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
                p: '10px 20px', bgcolor: 'action.hover', mt: 'auto',
            }}>
                <Switch
                    size="small"
                    checked={c.isActive}
                    onChange={onToggle}
                    sx={{
                        '& .MuiSwitch-switchBase.Mui-checked': { color: 'success.main' },
                        '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: 'success.main' },
                    }}
                />
                <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary' }}>
                    {c.isActive ? 'Enabled' : 'Disabled'}
                </Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
                    · {c.isActive ? 'in the component catalogue' : 'hidden from pickers'}
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
                borderRadius: '12px', p: '40px 20px', minHeight: 200,
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
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>Create new component</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5 }}>
                Add a deliverable projects<br/>are made up of
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

function ComponentFormDialog(props: {
    open: boolean
    title: string
    initial?: ProjectComponent
    isPending: boolean
    error: Error | null
    onClose: () => void
    onSubmit: (payload: UpsertProjectComponentRequest) => void
}) {
    const i = props.initial
    const [name, setName] = useState(i?.name ?? '')
    const [icon, setIcon] = useState(i?.icon ?? DEFAULT_ICON)
    const [colorKey, setColorKey] = useState<string>(i?.colorKey ?? 'default')
    const [description, setDescription] = useState(i?.description ?? '')
    const [isActive, setIsActive] = useState(i?.isActive ?? true)

    const submit = () => {
        props.onSubmit({
            name: name.trim(),
            description: description.trim(),
            icon: icon.trim() || DEFAULT_ICON,
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

export default ComponentsPanel
