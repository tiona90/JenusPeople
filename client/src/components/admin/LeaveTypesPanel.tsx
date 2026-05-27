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
    createLeaveType,
    deleteLeaveType,
    getAnnualLeaves,
    getLeaveTypes,
    updateLeaveType,
    type UpsertLeaveTypeRequest,
} from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'
import type {
    AttachmentPolicy,
    EligibilityScope,
    LeaveType,
} from '../../lib/types'

/* ─── tokens ─────────────────────────────────────────────────────────────── */


const PROTECTED_NAME = 'annual leave'

const HEADER_GRADIENTS: Record<string, string> = {
    annual:      'linear-gradient(135deg, #DBEAFE 0%, #BFDBFE 100%)',
    sick:        'linear-gradient(135deg, #FEE2E2 0%, #FECACA 100%)',
    personal:    'linear-gradient(135deg, #E0E7FF 0%, #C7D2FE 100%)',
    bereavement: 'linear-gradient(135deg, #E5E7EB 0%, #D1D5DB 100%)',
    unpaid:      'linear-gradient(135deg, #F3F4F6 0%, #E5E7EB 100%)',
    maternity:   'linear-gradient(135deg, #FCE7F3 0%, #FBCFE8 100%)',
    paternity:   'linear-gradient(135deg, #DBEAFE 0%, #BAE6FD 100%)',
    default:     'linear-gradient(135deg, #F1F5F9 0%, #E2E8F0 100%)',
}

const COLOR_KEYS = ['annual', 'sick', 'personal', 'bereavement', 'unpaid', 'maternity', 'paternity', 'default']

type StatusFilter = 'all' | 'enabled' | 'disabled'
type CategoryFilter = 'all' | 'paid' | 'unpaid' | 'special'

interface DerivedType {
    type: LeaveType
    requestsYTD: number
    daysTakenYTD: number
    avgRequest: number
    isMostUsed: boolean
}

function getErrorMessage(error: unknown) {
    return getApiErrorMessage(error, 'Something went wrong. Please try again.')
}

function isSpecial(t: LeaveType) {
    return t.attachmentPolicy === 'Required' || t.eligibilityScope === 'Limited'
}

function gradientFor(colorKey: string) {
    return HEADER_GRADIENTS[colorKey] ?? HEADER_GRADIENTS.default
}

/* ════════════════════════════════════════════════════════════════════════ */

function LeaveTypesPanel() {
    const queryClient = useQueryClient()

    const [createOpen, setCreateOpen] = useState(false)
    const [editType, setEditType] = useState<LeaveType | null>(null)
    const [searchText, setSearchText] = useState('')
    const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
    const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all')

    const { data: leaveTypes = [], isLoading, isError, error } = useQuery({
        queryKey: ['leaveTypes'],
        queryFn: getLeaveTypes,
    })
    const { data: annualLeaves = [] } = useQuery({
        queryKey: ['annualLeaves'],
        queryFn: getAnnualLeaves,
    })

    const currentYear = new Date().getFullYear()

    const derivedAll: DerivedType[] = useMemo(() => {
        // Aggregate by type id
        const aggBy = new Map<number, { requests: number; days: number }>()
        for (const leave of annualLeaves) {
            if (leave.leaveTypeId == null) continue
            if (leave.status !== 'Approved' && leave.status !== 'Pending') continue
            if (new Date(leave.startDate).getFullYear() !== currentYear) continue
            const agg = aggBy.get(leave.leaveTypeId) ?? { requests: 0, days: 0 }
            agg.requests += 1
            agg.days += leave.totalDays
            aggBy.set(leave.leaveTypeId, agg)
        }

        let topId: number | null = null
        let topCount = -1
        for (const t of leaveTypes) {
            const v = aggBy.get(t.id)
            if (!t.isActive || !v) continue
            if (v.requests > topCount) {
                topCount = v.requests
                topId = t.id
            }
        }

        return leaveTypes.map((t) => {
            const v = aggBy.get(t.id) ?? { requests: 0, days: 0 }
            return {
                type: t,
                requestsYTD: v.requests,
                daysTakenYTD: v.days,
                avgRequest: v.requests > 0 ? +(v.days / v.requests).toFixed(1) : 0,
                isMostUsed: t.id === topId,
            }
        })
    }, [leaveTypes, annualLeaves, currentYear])

    const filtered = useMemo(() => {
        let out = derivedAll
        if (statusFilter === 'enabled') out = out.filter((d) => d.type.isActive)
        else if (statusFilter === 'disabled') out = out.filter((d) => !d.type.isActive)

        if (categoryFilter === 'paid') out = out.filter((d) => d.type.paid && !isSpecial(d.type))
        else if (categoryFilter === 'unpaid') out = out.filter((d) => !d.type.paid)
        else if (categoryFilter === 'special') out = out.filter((d) => isSpecial(d.type))

        if (searchText.trim()) {
            const q = searchText.trim().toLowerCase()
            out = out.filter((d) =>
                d.type.name.toLowerCase().includes(q) ||
                d.type.description.toLowerCase().includes(q)
            )
        }
        return [...out].sort((a, b) => a.type.name.localeCompare(b.type.name))
    }, [derivedAll, statusFilter, categoryFilter, searchText])

    /* Aggregate stats */
    const totalActive = derivedAll.filter((d) => d.type.isActive).length
    const totalRequestsYTD = derivedAll.reduce((s, d) => s + d.requestsYTD, 0)
    const totalDaysYTD = derivedAll.reduce((s, d) => s + d.daysTakenYTD, 0)
    const mostUsed = derivedAll
        .filter((d) => d.type.isActive)
        .sort((a, b) => b.requestsYTD - a.requestsYTD)[0]

    /* Mutations */
    const createMutation = useMutation({
        mutationFn: createLeaveType,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['leaveTypes'] })
            setCreateOpen(false)
        },
    })
    const updateMutation = useMutation({
        mutationFn: ({ id, payload }: { id: number; payload: UpsertLeaveTypeRequest }) =>
            updateLeaveType(id, payload),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['leaveTypes'] })
            setEditType(null)
        },
    })
    const deleteMutation = useMutation({
        mutationFn: deleteLeaveType,
        onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['leaveTypes'] }),
    })

    const toggleActive = (t: LeaveType) => {
        const payload: UpsertLeaveTypeRequest = {
            name: t.name,
            requiresApproval: t.requiresApproval,
            isActive: !t.isActive,
            affectsBalance: t.affectsBalance,
            icon: t.icon,
            colorKey: t.colorKey,
            description: t.description,
            paid: t.paid,
            attachmentPolicy: t.attachmentPolicy,
            defaultAllowance: t.defaultAllowance,
            allowanceUnit: t.allowanceUnit,
            accrualNotes: t.accrualNotes,
            minNoticeDays: t.minNoticeDays,
            maxConsecutiveDays: t.maxConsecutiveDays,
            halfDayAllowed: t.halfDayAllowed,
            eligibilityNotes: t.eligibilityNotes,
            eligibilityScope: t.eligibilityScope,
        }
        updateMutation.mutate({ id: t.id, payload })
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
                    label="🏷️ Active Types"
                    value={String(totalActive)}
                    sub={`of ${derivedAll.length} configured · ${derivedAll.length - totalActive} disabled`}
                />
                <StatCard
                    label="📊 Requests YTD"
                    value={String(totalRequestsYTD)}
                    valueColor={'primary.main'}
                    sub={`across all leave types · avg ${totalActive > 0 ? Math.round(totalRequestsYTD / totalActive) : 0} per type`}
                />
                <StatCard
                    label="📅 Total Days Taken"
                    value={String(totalDaysYTD)}
                    valueColor={'success.main'}
                    sub="days off used by all employees"
                />
                <StatCard
                    label="⭐ Most Used"
                    value={mostUsed ? `${mostUsed.type.icon} ${mostUsed.type.name.replace(' Leave', '').replace(' Days', '')}` : '—'}
                    valueSize={18}
                    sub={mostUsed && totalRequestsYTD > 0
                        ? `${mostUsed.requestsYTD} requests · ${Math.round((mostUsed.requestsYTD / totalRequestsYTD) * 100)}% of all`
                        : 'no usage this year'}
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
                        placeholder="Search leave types…"
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
                        { value: 'all', label: `All statuses (${derivedAll.length})` },
                        { value: 'enabled', label: `Enabled (${totalActive})` },
                        { value: 'disabled', label: `Disabled (${derivedAll.length - totalActive})` },
                    ]}
                />
                <SelectFilter
                    value={categoryFilter}
                    onChange={(v) => setCategoryFilter(v as CategoryFilter)}
                    options={[
                        { value: 'all', label: 'All categories' },
                        { value: 'paid', label: 'Paid leave' },
                        { value: 'unpaid', label: 'Unpaid leave' },
                        { value: 'special', label: 'Special leave' },
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
                    + New leave type
                </Box>
            </Box>

            {/* Cards grid */}
            {filtered.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    No leave types match the current filters.
                </Box>
            ) : (
                <Box sx={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(340px, 1fr))',
                    gap: '14px',
                }}>
                    {filtered.map((d) => (
                        <LeaveTypeCard
                            key={d.type.id}
                            derived={d}
                            onEdit={() => setEditType(d.type)}
                            onToggle={() => toggleActive(d.type)}
                            onDelete={async () => {
                                const isProtected = d.type.name.trim().toLowerCase() === PROTECTED_NAME
                                if (isProtected) {
                                    await SweetAlert.fire({
                                        title: 'Protected leave type',
                                        text: 'Annual Leave cannot be deleted.',
                                        icon: 'info',
                                    })
                                    return
                                }
                                const result = await SweetAlert.fire({
                                    title: `Delete "${d.type.name}"?`,
                                    text: 'This will fail if leave requests use it.',
                                    icon: 'warning',
                                    showCancelButton: true,
                                    confirmButtonText: 'Yes, delete',
                                    cancelButtonText: 'Cancel',
                                    confirmButtonColor: '#EF4444',
                                    reverseButtons: true,
                                })
                                if (result.isConfirmed) deleteMutation.mutate(d.type.id)
                            }}
                        />
                    ))}
                    <AddCard onClick={() => setCreateOpen(true)} />
                </Box>
            )}

            <LeaveTypeFormDialog
                key={createOpen ? 'lt-create-open' : 'lt-create-closed'}
                open={createOpen}
                title="New Leave Type"
                isPending={createMutation.isPending}
                error={createMutation.error}
                onClose={() => setCreateOpen(false)}
                onSubmit={(payload) => createMutation.mutate(payload)}
            />

            <LeaveTypeFormDialog
                key={editType ? `lt-edit-${editType.id}` : 'lt-edit-none'}
                open={!!editType}
                title="Edit Leave Type"
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

function LeaveTypeCard({ derived, onEdit, onToggle, onDelete }: {
    derived: DerivedType
    onEdit: () => void
    onToggle: () => void
    onDelete: () => void
}) {
    const { type: t, requestsYTD, daysTakenYTD, avgRequest, isMostUsed } = derived
    const isProtected = t.name.trim().toLowerCase() === PROTECTED_NAME

    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px',
            overflow: 'hidden', transition: 'all 0.15s',
            display: 'flex', flexDirection: 'column',
            opacity: t.isActive ? 1 : 0.65,
            '&:hover': { transform: 'translateY(-2px)', boxShadow: '0 6px 20px rgba(0,0,0,0.06)' },
        }}>
            {/* Header */}
            <Box sx={{
                p: '18px 20px', position: 'relative', overflow: 'hidden',
                borderBottom: '1px solid #F3F4F6',
                background: t.isActive ? gradientFor(t.colorKey) : '#E5E7EB',
            }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <Box sx={{ minWidth: 0 }}>
                        <Box sx={{
                            fontSize: 36, lineHeight: 1, mb: '10px',
                            filter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.1))',
                        }}>
                            {t.icon}
                        </Box>
                        <Box sx={{ fontSize: 18, fontWeight: 700, color: 'text.primary', lineHeight: 1.2, mb: '4px' }}>
                            {t.name}
                        </Box>
                        {t.description && (
                            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5, maxWidth: '90%' }}>
                                {t.description}
                            </Box>
                        )}
                    </Box>
                    <Box sx={{ display: 'flex', gap: '4px' }}>
                        <HeaderIconBtn title="Edit" onClick={onEdit}>✏️</HeaderIconBtn>
                        {!isProtected && (
                            <HeaderIconBtn title="Delete" onClick={onDelete}>🗑</HeaderIconBtn>
                        )}
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
                    checked={t.isActive}
                    onChange={onToggle}
                    disabled={isProtected && t.isActive}
                    sx={{
                        '& .MuiSwitch-switchBase.Mui-checked': { color: 'success.main' },
                        '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: 'success.main' },
                    }}
                />
                <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary' }}>
                    {t.isActive ? 'Enabled' : 'Disabled'}
                </Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
                    · {t.isActive ? 'available to employees' : 'hidden from leave requests'}
                </Box>
            </Box>

            {/* Allowance */}
            <Box sx={{
                p: '16px 20px', borderBottom: '1px solid #F3F4F6',
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px',
            }}>
                <Box sx={{ display: 'flex', flexDirection: 'column' }}>
                    <Box sx={{ fontSize: 10, color: 'text.disabled', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600, mb: '2px' }}>
                        Default allowance
                    </Box>
                    <Box sx={{ fontSize: 28, fontWeight: 700, color: 'text.primary', lineHeight: 1 }}>
                        {t.defaultAllowance}
                        <Box component="span" sx={{ fontSize: 14, color: 'text.secondary', fontWeight: 500, ml: '4px' }}>
                            {t.allowanceUnit}
                        </Box>
                    </Box>
                    {t.accrualNotes && (
                        <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>{t.accrualNotes}</Box>
                    )}
                </Box>
                <Box
                    component="button"
                    onClick={onEdit}
                    sx={{
                        fontSize: 11, color: 'primary.main', bgcolor: 'transparent',
                        border: '1px solid #C7D7F7', px: '10px', py: '4px',
                        borderRadius: '6px', cursor: 'pointer', fontFamily: 'inherit', fontWeight: 500,
                        '&:hover': { bgcolor: softBg('primary') },
                    }}
                >
                    Edit
                </Box>
            </Box>

            {/* Rules */}
            <Box sx={{ p: '12px 20px', borderBottom: '1px solid #F3F4F6' }}>
                <Box sx={{ fontSize: 10, color: 'text.disabled', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600, mb: '10px' }}>
                    Rules & approval
                </Box>
                <Rule
                    ok={t.paid}
                    label={t.paid ? <><strong>Paid leave</strong> · counts against balance</> : <><strong>Unpaid</strong> · no balance deduction, no pay</>}
                />
                <Rule
                    ok={t.requiresApproval}
                    label={t.requiresApproval ? <strong>Requires manager approval</strong> : <>Auto-approved (no manager review)</>}
                />
                <AttachmentRule policy={t.attachmentPolicy} />
                <Rule
                    ok={true}
                    glyph={t.minNoticeDays > 0 ? '📅' : '⚡'}
                    label={t.minNoticeDays > 0
                        ? <>Minimum <strong>{t.minNoticeDays} days notice</strong> required</>
                        : <>Same-day requests allowed</>}
                />
                {t.halfDayAllowed && <Rule ok={true} label={<>Half-day requests allowed</>} />}
                {t.maxConsecutiveDays > 0 && (
                    <Rule
                        ok={true}
                        glyph="📏"
                        label={<>Max <strong>{t.maxConsecutiveDays} consecutive days</strong> per request</>}
                    />
                )}
            </Box>

            {/* Eligibility */}
            <Box sx={{
                p: '10px 20px', display: 'flex', flexWrap: 'wrap', gap: '4px',
                alignItems: 'center', bgcolor: 'action.hover',
            }}>
                <Box sx={{ fontSize: 10, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600, mr: '4px' }}>
                    Available to
                </Box>
                <Box sx={{
                    display: 'inline-flex', alignItems: 'center', gap: '3px',
                    px: '8px', py: '2px', borderRadius: '10px',
                    fontSize: 10, fontWeight: 500,
                    bgcolor: t.eligibilityScope === 'All' ? softBg('success') : softBg('primary'),
                    color: t.eligibilityScope === 'All' ? 'success.dark' : 'info.dark',
                }}>
                    {t.eligibilityNotes || (t.eligibilityScope === 'All' ? 'All employees' : 'Limited')}
                </Box>
            </Box>

            {/* Footer stats */}
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '1px', bgcolor: 'divider', mt: 'auto' }}>
                <FooterStat
                    label="Requests YTD"
                    value={String(requestsYTD)}
                    sub={isMostUsed && requestsYTD > 0 ? '★ most used' : 'employees'}
                />
                <FooterStat label="Days taken" value={String(daysTakenYTD)} sub="YTD across org" />
                <FooterStat label="Avg request" value={avgRequest > 0 ? String(avgRequest) : '—'} sub="days per request" />
            </Box>
        </Box>
    )
}

function AttachmentRule({ policy }: { policy: AttachmentPolicy }) {
    if (policy === 'Required') {
        return (
            <Rule
                ok={true}
                label={<><strong>Attachment required</strong> (e.g. medical certificate, birth certificate)</>}
            />
        )
    }
    if (policy === 'Optional') {
        return (
            <Rule
                ok={true}
                glyph="~"
                glyphColor={'warning.main'}
                label={<>Attachment <strong>encouraged</strong> (faster approval with doctor's note)</>}
            />
        )
    }
    return <Rule ok={false} label={<>No attachment needed</>} />
}

function Rule({ ok, label, glyph, glyphColor }: {
    ok: boolean
    label: React.ReactNode
    glyph?: string
    glyphColor?: string
}) {
    return (
        <Box sx={{
            display: 'grid', gridTemplateColumns: '18px 1fr', gap: '8px',
            py: '5px', alignItems: 'flex-start',
            fontSize: 12, color: 'text.primary',
        }}>
            <Box sx={{
                color: glyphColor ?? (ok ? 'success.main' : 'text.disabled'),
                fontWeight: 700, lineHeight: 1.5,
            }}>
                {glyph ?? (ok ? '✓' : '○')}
            </Box>
            <Box sx={{ lineHeight: 1.5, '& strong': { color: 'text.primary', fontWeight: 600 } }}>
                {label}
            </Box>
        </Box>
    )
}

function FooterStat({ label, value, sub }: { label: string; value: string; sub: string }) {
    return (
        <Box sx={{ bgcolor: 'background.paper', p: '12px 14px', textAlign: 'center' }}>
            <Box sx={{ fontSize: 10, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '4px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 17, fontWeight: 700, color: 'text.primary', lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 10, color: 'text.secondary', mt: '2px' }}>{sub}</Box>
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
                borderRadius: '12px', p: '40px 20px', minHeight: 460,
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
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>Create a new leave type</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5 }}>
                Define allowance, approval rules,<br/>and eligibility for your organization
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

function LeaveTypeFormDialog(props: {
    open: boolean
    title: string
    initial?: LeaveType
    isPending: boolean
    error: Error | null
    onClose: () => void
    onSubmit: (payload: UpsertLeaveTypeRequest) => void
}) {
    const i = props.initial
    const [name, setName] = useState(i?.name ?? '')
    const [icon, setIcon] = useState(i?.icon ?? '🏷️')
    const [colorKey, setColorKey] = useState<string>(i?.colorKey ?? 'default')
    const [description, setDescription] = useState(i?.description ?? '')
    const [requiresApproval, setRequiresApproval] = useState(i?.requiresApproval ?? true)
    const [isActive, setIsActive] = useState(i?.isActive ?? true)
    const [affectsBalance, setAffectsBalance] = useState(i?.affectsBalance ?? false)
    const [paid, setPaid] = useState(i?.paid ?? true)
    const [attachmentPolicy, setAttachmentPolicy] = useState<AttachmentPolicy>(i?.attachmentPolicy ?? 'None')
    const [defaultAllowance, setDefaultAllowance] = useState<number>(i?.defaultAllowance ?? 0)
    const [allowanceUnit, setAllowanceUnit] = useState(i?.allowanceUnit ?? 'days/year')
    const [accrualNotes, setAccrualNotes] = useState(i?.accrualNotes ?? '')
    const [minNoticeDays, setMinNoticeDays] = useState<number>(i?.minNoticeDays ?? 0)
    const [maxConsecutiveDays, setMaxConsecutiveDays] = useState<number>(i?.maxConsecutiveDays ?? 0)
    const [halfDayAllowed, setHalfDayAllowed] = useState(i?.halfDayAllowed ?? false)
    const [eligibilityNotes, setEligibilityNotes] = useState(i?.eligibilityNotes ?? 'All employees')
    const [eligibilityScope, setEligibilityScope] = useState<EligibilityScope>(i?.eligibilityScope ?? 'All')

    useEffect(() => {
        if (!props.open) return
        const x = props.initial
        setName(x?.name ?? '')
        setIcon(x?.icon ?? '🏷️')
        setColorKey(x?.colorKey ?? 'default')
        setDescription(x?.description ?? '')
        setRequiresApproval(x?.requiresApproval ?? true)
        setIsActive(x?.isActive ?? true)
        setAffectsBalance(x?.affectsBalance ?? false)
        setPaid(x?.paid ?? true)
        setAttachmentPolicy(x?.attachmentPolicy ?? 'None')
        setDefaultAllowance(x?.defaultAllowance ?? 0)
        setAllowanceUnit(x?.allowanceUnit ?? 'days/year')
        setAccrualNotes(x?.accrualNotes ?? '')
        setMinNoticeDays(x?.minNoticeDays ?? 0)
        setMaxConsecutiveDays(x?.maxConsecutiveDays ?? 0)
        setHalfDayAllowed(x?.halfDayAllowed ?? false)
        setEligibilityNotes(x?.eligibilityNotes ?? 'All employees')
        setEligibilityScope(x?.eligibilityScope ?? 'All')
    }, [props.open, props.initial])

    const submit = () => {
        props.onSubmit({
            name: name.trim(),
            icon: icon.trim() || '🏷️',
            colorKey,
            description: description.trim(),
            requiresApproval,
            isActive,
            affectsBalance,
            paid,
            attachmentPolicy,
            defaultAllowance: Number(defaultAllowance) || 0,
            allowanceUnit: allowanceUnit.trim() || 'days/year',
            accrualNotes: accrualNotes.trim(),
            minNoticeDays: Number(minNoticeDays) || 0,
            maxConsecutiveDays: Number(maxConsecutiveDays) || 0,
            halfDayAllowed,
            eligibilityNotes: eligibilityNotes.trim() || 'All employees',
            eligibilityScope,
        })
    }

    return (
        <AppDialog open={props.open} onClose={props.onClose} maxWidth="sm">
            <AppDialogTitle>{props.title}</AppDialogTitle>
            <AppDialogContent>
                <Stack spacing={2}>
                    {/* Identity */}
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

                    {/* Allowance */}
                    <Stack direction="row" spacing={2}>
                        <TextField
                            label="Default allowance"
                            type="number"
                            value={defaultAllowance}
                            onChange={(e) => setDefaultAllowance(Number(e.target.value))}
                            inputProps={{ min: 0, max: 365 }}
                            sx={{ width: 180 }}
                        />
                        <TextField
                            label="Unit"
                            value={allowanceUnit}
                            onChange={(e) => setAllowanceUnit(e.target.value)}
                            fullWidth
                            helperText="e.g. days/year, days/event"
                        />
                    </Stack>
                    <TextField
                        label="Accrual notes"
                        value={accrualNotes}
                        onChange={(e) => setAccrualNotes(e.target.value)}
                        fullWidth
                        inputProps={{ maxLength: 250 }}
                        helperText="e.g. Resets 1 Jan · No carryover"
                    />

                    {/* Rules */}
                    <Stack direction="row" spacing={2}>
                        <TextField
                            label="Min notice (days)"
                            type="number"
                            value={minNoticeDays}
                            onChange={(e) => setMinNoticeDays(Number(e.target.value))}
                            inputProps={{ min: 0, max: 365 }}
                            fullWidth
                        />
                        <TextField
                            label="Max consecutive (days)"
                            type="number"
                            value={maxConsecutiveDays}
                            onChange={(e) => setMaxConsecutiveDays(Number(e.target.value))}
                            inputProps={{ min: 0, max: 365 }}
                            fullWidth
                            helperText="0 = no maximum"
                        />
                    </Stack>

                    <TextField
                        select
                        label="Attachment policy"
                        value={attachmentPolicy}
                        onChange={(e) => setAttachmentPolicy(e.target.value as AttachmentPolicy)}
                        fullWidth
                    >
                        <MenuItem value="None">No attachment needed</MenuItem>
                        <MenuItem value="Optional">Attachment encouraged</MenuItem>
                        <MenuItem value="Required">Attachment required</MenuItem>
                    </TextField>

                    {/* Toggles */}
                    <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1 }}>
                        <FormControlLabel
                            control={<Switch checked={paid} onChange={(e) => setPaid(e.target.checked)} />}
                            label="Paid leave"
                        />
                        <FormControlLabel
                            control={<Switch checked={requiresApproval} onChange={(e) => setRequiresApproval(e.target.checked)} />}
                            label="Requires approval"
                        />
                        <FormControlLabel
                            control={<Switch checked={affectsBalance} onChange={(e) => setAffectsBalance(e.target.checked)} />}
                            label="Affects leave balance"
                        />
                        <FormControlLabel
                            control={<Switch checked={halfDayAllowed} onChange={(e) => setHalfDayAllowed(e.target.checked)} />}
                            label="Half-day allowed"
                        />
                        <FormControlLabel
                            control={<Switch checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />}
                            label="Active"
                        />
                    </Box>

                    {/* Eligibility */}
                    <Stack direction="row" spacing={2}>
                        <TextField
                            select
                            label="Eligibility scope"
                            value={eligibilityScope}
                            onChange={(e) => setEligibilityScope(e.target.value as EligibilityScope)}
                            sx={{ width: 180 }}
                        >
                            <MenuItem value="All">All employees</MenuItem>
                            <MenuItem value="Limited">Limited</MenuItem>
                        </TextField>
                        <TextField
                            label="Eligibility notes"
                            value={eligibilityNotes}
                            onChange={(e) => setEligibilityNotes(e.target.value)}
                            fullWidth
                            inputProps={{ maxLength: 250 }}
                            helperText="Shown as the eligibility chip"
                        />
                    </Stack>

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

export default LeaveTypesPanel
