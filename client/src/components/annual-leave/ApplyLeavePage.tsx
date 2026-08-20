import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import { createAnnualLeave, getAnnualLeaves, getEmployeeProfiles, getHolidays, getLeaveTypes, getTeammates, uploadLeaveEvidence } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'
import { AppDialog, AppDialogActions, AppDialogContent, AppDialogTitle, cancelBtnSx } from '../ui'
import Button from '@mui/material/Button'
import { iconForLeaveType } from './leave-icons'
import type { LeaveType, Teammate, UserInfo } from '../../lib/types'
import { softBg, type SxColor } from '../../lib/theme-tokens'
import type { Theme } from '@mui/material/styles'

const applyLeaveSchema = z
    .object({
        leaveTypeId: z.number().int().positive('Choose a leave type to continue.'),
        duration: z.enum(['full', 'half-am', 'half-pm']),
        startDate: z.string().min(1, 'Pick a start date on the calendar.'),
        endDate: z.string().min(1, 'Pick an end date on the calendar.'),
        reason: z.string().max(500, 'Reason must be 500 characters or fewer.').optional(),
        delegateId: z.string().optional(),
    })
    .refine((data) => !data.startDate || !data.endDate || data.endDate >= data.startDate, {
        message: 'End date must be on or after the start date.',
        path: ['endDate'],
    })

type ApplyLeaveFormValues = z.infer<typeof applyLeaveSchema>
type FileKind = 'pdf' | 'img' | 'doc' | 'other'

interface StagedFile {
    file: File
    name: string
    size: number
    kind: FileKind
    previewUrl: string | null
}

const MAX_FILE_BYTES = 10 * 1024 * 1024
const ALLOWED_MIME = [
    'application/pdf',
    'image/jpeg', 'image/png', 'image/heic', 'image/webp',
    'application/msword',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
]

function classifyFile(file: File): FileKind {
    if (file.type.startsWith('image/')) return 'img'
    if (file.type === 'application/pdf') return 'pdf'
    if (file.type.includes('word') || /\.docx?$/i.test(file.name)) return 'doc'
    return 'other'
}

function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}


const QUICK_REASONS = [
    'Family vacation — booked in advance.',
    'Medical appointment.',
    'Personal matter.',
    'Wedding / family event.',
]

const QUICK_REASON_LABELS = ['Family vacation', 'Medical appointment', 'Personal matter', 'Family event']

const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']

function isoDate(d: Date): string {
    const y = d.getFullYear()
    const m = String(d.getMonth() + 1).padStart(2, '0')
    const day = String(d.getDate()).padStart(2, '0')
    return `${y}-${m}-${day}`
}

function workingDaysBetween(startIso: string, endIso: string, holidays?: Set<string>): number {
    if (!startIso || !endIso) return 0
    const s = new Date(startIso)
    const e = new Date(endIso)
    if (e < s) return 0
    let count = 0
    const curr = new Date(s)
    while (curr <= e) {
        const dow = curr.getDay()
        const iso = isoDate(curr)
        if (dow !== 0 && dow !== 6 && !holidays?.has(iso)) count++
        curr.setDate(curr.getDate() + 1)
    }
    return count
}

function daysNotice(startIso: string): number {
    if (!startIso) return 0
    const today = new Date()
    today.setHours(0, 0, 0, 0)
    const s = new Date(startIso)
    s.setHours(0, 0, 0, 0)
    return Math.round((s.getTime() - today.getTime()) / 86_400_000)
}

function formatDate(iso: string): string {
    if (!iso) return '—'
    const d = new Date(iso)
    return d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

function nextWorkingDay(iso: string, holidays?: Set<string>): string {
    if (!iso) return '—'
    const d = new Date(iso)
    d.setDate(d.getDate() + 1)
    while (d.getDay() === 0 || d.getDay() === 6 || holidays?.has(isoDate(d))) {
        d.setDate(d.getDate() + 1)
    }
    return d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

function descForLeaveType(name: string): string {
    const key = name.toLowerCase()
    if (key.includes('annual') || key.includes('vacation')) return 'Vacation, holidays'
    if (key.includes('sick')) return 'Illness, medical'
    if (key.includes('personal')) return 'Family, errands'
    if (key.includes('bereavement')) return 'Loss of loved one'
    if (key.includes('unpaid')) return 'No deduction from balance'
    if (key.includes('maternity') || key.includes('paternity') || key.includes('parental')) return 'New parent'
    return ''
}

function ApplyLeavePage({ user }: { user: UserInfo }) {
    const { uiStore } = useStore()
    const queryClient = useQueryClient()
    const today = new Date()

    const {
        watch,
        setValue,
        handleSubmit,
        formState: { errors },
    } = useForm<ApplyLeaveFormValues>({
        resolver: zodResolver(applyLeaveSchema),
        mode: 'onChange',
        defaultValues: {
            leaveTypeId: 0,
            duration: 'full',
            startDate: '',
            endDate: '',
            reason: '',
            delegateId: '',
        },
    })

    const leaveTypeId = watch('leaveTypeId')
    const duration = watch('duration')
    const startDate = watch('startDate')
    const endDate = watch('endDate')
    const reason = watch('reason') ?? ''
    const delegateId = watch('delegateId') ?? ''

    const [calMonth, setCalMonth] = useState<number>(today.getMonth())
    const [calYear, setCalYear] = useState<number>(today.getFullYear())
    const [attachment, setAttachment] = useState<StagedFile | null>(null)
    const [uploadError, setUploadError] = useState<string | null>(null)
    const [isDragOver, setIsDragOver] = useState(false)
    const [delegatePickerOpen, setDelegatePickerOpen] = useState(false)
    const [delegateSearch, setDelegateSearch] = useState('')
    const fileInputRef = useRef<HTMLInputElement>(null)

    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: allLeaves = [] } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: teammates = [] } = useQuery({ queryKey: ['teammates'], queryFn: getTeammates })

    // Holidays for the displayed calendar year (and the selection year if different)
    const { data: holidaysCurrentYear = [] } = useQuery({
        queryKey: ['holidays', calYear],
        queryFn: () => getHolidays(calYear),
        staleTime: 60 * 60 * 1000,
    })
    const selectionYear = startDate ? Number(startDate.slice(0, 4)) : calYear
    const { data: holidaysSelectionYear = [] } = useQuery({
        queryKey: ['holidays', selectionYear],
        queryFn: () => getHolidays(selectionYear),
        enabled: selectionYear !== calYear,
        staleTime: 60 * 60 * 1000,
    })

    const holidayMap = useMemo(() => {
        const map = new Map<string, string>()
        const add = (h: { date: string; localName: string; englishName: string }) => {
            const iso = h.date.slice(0, 10)
            map.set(iso, h.localName || h.englishName)
        }
        holidaysCurrentYear.forEach(add)
        holidaysSelectionYear.forEach(add)
        return map
    }, [holidaysCurrentYear, holidaysSelectionYear])
    const holidaySet = useMemo(() => new Set(holidayMap.keys()), [holidayMap])

    const activeLeaveTypes = useMemo(() => leaveTypes.filter((lt) => lt.isActive), [leaveTypes])

    const myProfile = profiles.find((p) => p.userId === user.id)
    const entitlement = myProfile?.annualLeaveEntitlement ?? 0

    const usedDays = useMemo(() => {
        const year = new Date().getFullYear()
        return allLeaves
            .filter((l) => l.employeeId === user.id && l.status === 'Approved' && new Date(l.startDate).getFullYear() === year)
            .reduce((sum, l) => sum + l.totalDays, 0)
    }, [allLeaves, user.id])

    // Pick a sensible default leave type once data loads
    useEffect(() => {
        if (leaveTypeId === 0 && activeLeaveTypes.length > 0) {
            const annual = activeLeaveTypes.find((lt) => lt.name.toLowerCase().includes('annual')) ?? activeLeaveTypes[0]
            setValue('leaveTypeId', annual.id, { shouldValidate: true })
        }
    }, [activeLeaveTypes, leaveTypeId, setValue])

    const selectedType = activeLeaveTypes.find((lt) => lt.id === leaveTypeId)
    const selectedAffectsBalance = selectedType?.affectsBalance ?? true

    const currentBalance = Math.max(0, entitlement - usedDays)
    const workingDays = workingDaysBetween(startDate, endDate, holidaySet)
    const daysDeducted = duration === 'full' ? workingDays : workingDays > 0 ? workingDays * 0.5 : 0
    const balanceAfter = selectedAffectsBalance ? currentBalance - daysDeducted : currentBalance
    const balancePct = entitlement > 0 ? Math.min(100, ((usedDays + (selectedAffectsBalance ? daysDeducted : 0)) / entitlement) * 100) : 0
    const notice = daysNotice(startDate)
    const isShortNotice = !!startDate && notice >= 0 && notice < 7
    const isInsufficient = selectedAffectsBalance && balanceAfter < 0

    // Conflicts: teammates in same department with overlapping approved/pending leave
    const conflictNames = useMemo(() => {
        if (!startDate || !endDate || !user.departmentId) return []
        const teammates = profiles.filter(
            (p) => p.departmentId === user.departmentId && p.userId !== user.id
        )
        const teammateIds = new Set(teammates.map((p) => p.userId))
        const overlapping = allLeaves.filter(
            (l) =>
                teammateIds.has(l.employeeId) &&
                (l.status === 'Approved' || l.status === 'Pending') &&
                l.startDate <= endDate &&
                l.endDate >= startDate
        )
        const names = new Set<string>()
        overlapping.forEach((l) => names.add(l.employeeName))
        return Array.from(names)
    }, [allLeaves, profiles, user.departmentId, user.id, startDate, endDate])

    // Map: iso → array of teammate names on leave that day
    const teammateLeaveByDay = useMemo(() => {
        if (!user.departmentId) return new Map<string, string[]>()
        const map = new Map<string, string[]>()
        const teammates = profiles.filter(
            (p) => p.departmentId === user.departmentId && p.userId !== user.id
        )
        const teammateIds = new Set(teammates.map((p) => p.userId))
        allLeaves
            .filter((l) => teammateIds.has(l.employeeId) && (l.status === 'Approved' || l.status === 'Pending'))
            .forEach((l) => {
                const s = new Date(l.startDate)
                const e = new Date(l.endDate)
                for (let d = new Date(s); d <= e; d.setDate(d.getDate() + 1)) {
                    const iso = isoDate(d)
                    const list = map.get(iso) ?? []
                    list.push(l.employeeName)
                    map.set(iso, list)
                }
            })
        return map
    }, [allLeaves, profiles, user.departmentId])

    // Coverage candidates: department colleagues, minus yourself as a safety net
    // (the server already excludes the caller).
    const delegateCandidates = useMemo(
        () => teammates.filter((t) => t.userId !== user.id),
        [teammates, user.id]
    )

    // Anyone already off during the selected dates — nominating them is a poor choice.
    const awayUserIds = useMemo(() => {
        if (!startDate || !endDate) return new Set<string>()
        return new Set(
            allLeaves
                .filter(
                    (l) =>
                        (l.status === 'Approved' || l.status === 'Pending') &&
                        l.startDate <= endDate &&
                        l.endDate >= startDate
                )
                .map((l) => l.employeeId)
        )
    }, [allLeaves, startDate, endDate])

    const visibleDelegates = useMemo(() => {
        const term = delegateSearch.trim().toLowerCase()
        if (!term) return delegateCandidates
        return delegateCandidates.filter(
            (t) =>
                t.displayName.toLowerCase().includes(term) ||
                (t.jobTitle ?? '').toLowerCase().includes(term)
        )
    }, [delegateCandidates, delegateSearch])

    const selectedDelegate = delegateId
        ? delegateCandidates.find((t) => t.userId === delegateId) ?? null
        : null
    const delegateIsAway = !!selectedDelegate && awayUserIds.has(selectedDelegate.userId)

    function chooseDelegate(userId: string) {
        setValue('delegateId', userId, { shouldDirty: true, shouldValidate: true })
        closeDelegatePicker()
    }

    function clearDelegate() {
        setValue('delegateId', '', { shouldDirty: true, shouldValidate: true })
    }

    function closeDelegatePicker() {
        setDelegatePickerOpen(false)
        setDelegateSearch('')
    }

    const typeNameLower = selectedType?.name.toLowerCase() ?? ''
    const isSickLeave = typeNameLower.includes('sick')
    const isBereavement = typeNameLower.includes('bereavement')
    const attachmentRecommended = isSickLeave && !attachment

    const canSubmit = !!startDate && !!endDate && leaveTypeId > 0 && !isInsufficient

    const uploadMutation = useMutation({
        mutationFn: (file: File) => uploadLeaveEvidence(file),
    })

    const submitMutation = useMutation({
        mutationFn: async (values: ApplyLeaveFormValues) => {
            let evidenceUrl: string | undefined
            if (attachment) {
                const result = await uploadMutation.mutateAsync(attachment.file)
                evidenceUrl = result.evidenceUrl
            }
            return createAnnualLeave({
                employeeId: user.id,
                leaveTypeId: values.leaveTypeId,
                startDate: values.startDate,
                endDate: values.endDate,
                reason: (values.reason ?? '').trim() || '—',
                evidenceUrl,
                delegateId: values.delegateId?.trim() || undefined,
            })
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            uiStore.navigateToMyLeave('requests')
        },
    })

    const onSubmit = handleSubmit((values) => {
        if (isInsufficient) return
        submitMutation.mutate(values)
    })

    const isPending = uploadMutation.isPending || submitMutation.isPending
    const submitError = submitMutation.error ?? uploadMutation.error

    // Revoke preview object URL on unmount / change
    useEffect(() => {
        return () => {
            if (attachment?.previewUrl) URL.revokeObjectURL(attachment.previewUrl)
        }
    }, [attachment])

    function acceptFiles(fileList: FileList | null) {
        if (!fileList || fileList.length === 0) return
        const file = fileList[0] // backend stores a single evidence URL
        setUploadError(null)

        if (file.size > MAX_FILE_BYTES) {
            setUploadError(`"${file.name}" is over 10 MB.`)
            return
        }
        const kind = classifyFile(file)
        const looksAllowed = ALLOWED_MIME.includes(file.type) || kind !== 'other'
        if (!looksAllowed) {
            setUploadError(`"${file.name}" isn't an allowed file type.`)
            return
        }
        if (attachment?.previewUrl) URL.revokeObjectURL(attachment.previewUrl)
        setAttachment({
            file,
            name: file.name,
            size: file.size,
            kind,
            previewUrl: kind === 'img' ? URL.createObjectURL(file) : null,
        })
    }

    function removeAttachment() {
        if (attachment?.previewUrl) URL.revokeObjectURL(attachment.previewUrl)
        setAttachment(null)
        setUploadError(null)
        if (fileInputRef.current) fileInputRef.current.value = ''
    }

    function pickDate(iso: string) {
        const opts = { shouldValidate: true, shouldDirty: true } as const
        if (!startDate || (startDate && endDate)) {
            setValue('startDate', iso, opts)
            setValue('endDate', '', opts)
        } else if (iso < startDate) {
            setValue('startDate', iso, opts)
            setValue('endDate', '', opts)
        } else {
            setValue('endDate', iso, opts)
        }
    }

    function navMonth(delta: number) {
        let m = calMonth + delta
        let y = calYear
        if (m < 0) { m = 11; y-- }
        else if (m > 11) { m = 0; y++ }
        setCalMonth(m)
        setCalYear(y)
    }

    // Build calendar cells
    const firstOfMonth = new Date(calYear, calMonth, 1)
    const startDow = firstOfMonth.getDay()
    const startOffset = startDow === 0 ? 6 : startDow - 1
    const daysInMonth = new Date(calYear, calMonth + 1, 0).getDate()
    const todayIso = isoDate(new Date())

    const summaryBig = workingDays === 0 ? '—' : `${daysDeducted} ${daysDeducted === 1 ? 'day' : 'days'}`

    return (
        <Box
            component="form"
            onSubmit={onSubmit}
            noValidate
            sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', md: '1fr 340px' },
                gap: '18px',
                maxWidth: 1100,
            }}
        >
            {/* LEFT COLUMN */}
            <Box>
                {/* Step 1: Leave type */}
                <Box sx={sectionSx}>
                    <Box sx={sectionTitleSx}>
                        <Box component="span" sx={sectionNumSx}>1</Box>
                        What kind of leave?
                    </Box>
                    <Box sx={sectionSubSx}>Pick a type — your remaining balance is shown on each.</Box>
                    <Box sx={{
                        display: 'grid',
                        gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(3, 1fr)' },
                        gap: '10px',
                    }}>
                        {activeLeaveTypes.map((lt) => (
                            <LeaveTypeCard
                                key={lt.id}
                                type={lt}
                                selected={lt.id === leaveTypeId}
                                entitlement={entitlement}
                                used={usedDays}
                                onSelect={() => setValue('leaveTypeId', lt.id, { shouldValidate: true, shouldDirty: true })}
                            />
                        ))}
                    </Box>
                    {errors.leaveTypeId && (
                        <FieldError id="leaveTypeId-error">{errors.leaveTypeId.message}</FieldError>
                    )}
                </Box>

                {/* Step 2: Dates */}
                <Box sx={sectionSx}>
                    <Box sx={sectionTitleSx}>
                        <Box component="span" sx={sectionNumSx}>2</Box>
                        When?
                    </Box>
                    <Box sx={sectionSubSx}>
                        Click a start date, then an end date. Weekends are excluded automatically.
                    </Box>

                    {/* Duration toggle */}
                    <Box sx={{ display: 'flex', gap: '4px', p: '3px', bgcolor: 'action.hover', borderRadius: '8px', width: 'fit-content', mb: '14px' }}>
                        <DurationButton active={duration === 'full'} onClick={() => setValue('duration', 'full', { shouldDirty: true })}>Full day(s)</DurationButton>
                        <DurationButton active={duration === 'half-am'} onClick={() => setValue('duration', 'half-am', { shouldDirty: true })}>Half day (AM)</DurationButton>
                        <DurationButton active={duration === 'half-pm'} onClick={() => setValue('duration', 'half-pm', { shouldDirty: true })}>Half day (PM)</DurationButton>
                    </Box>

                    {/* Mini calendar */}
                    <Box sx={{ bgcolor: 'action.hover', border: '1px solid', borderColor: 'divider', borderRadius: '8px', p: '12px 14px' }}>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: '10px' }}>
                            <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                                {MONTH_NAMES[calMonth]} {calYear}
                            </Box>
                            <Box sx={{ display: 'flex', gap: '4px' }}>
                                <CalNavBtn onClick={() => navMonth(-1)}>‹</CalNavBtn>
                                <CalNavBtn onClick={() => navMonth(1)}>›</CalNavBtn>
                            </Box>
                        </Box>
                        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '3px', mb: '4px' }}>
                            {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map((d) => (
                                <Box key={d} sx={{ textAlign: 'center', fontSize: 10, color: 'text.disabled', fontWeight: 600, textTransform: 'uppercase', py: '4px' }}>{d}</Box>
                            ))}
                        </Box>
                        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '3px' }}>
                            {Array.from({ length: startOffset }).map((_, i) => (
                                <Box key={`blank-${i}`} sx={calCellSx({ otherMonth: true })} />
                            ))}
                            {Array.from({ length: daysInMonth }).map((_, i) => {
                                const day = i + 1
                                const d = new Date(calYear, calMonth, day)
                                const iso = isoDate(d)
                                const dow = d.getDay()
                                const isWeekend = dow === 0 || dow === 6
                                const teammates = teammateLeaveByDay.get(iso)
                                const holidayName = holidayMap.get(iso)
                                const isHoliday = !!holidayName
                                const isToday = iso === todayIso
                                const isStart = iso === startDate
                                const isEnd = iso === endDate
                                const inRange = !!startDate && !!endDate && iso > startDate && iso < endDate
                                const clickable = !isWeekend && !isHoliday
                                const tooltip = isHoliday
                                    ? `🎉 ${holidayName}`
                                    : teammates
                                        ? `${teammates.join(', ')} on leave`
                                        : ''
                                return (
                                    <Box
                                        key={iso}
                                        onClick={clickable ? () => pickDate(iso) : undefined}
                                        title={tooltip}
                                        sx={calCellSx({
                                            weekend: isWeekend,
                                            holiday: isHoliday,
                                            teammate: !!teammates,
                                            today: isToday,
                                            rangeStart: isStart,
                                            rangeEnd: isEnd,
                                            inRange,
                                        })}
                                    >
                                        {day}
                                    </Box>
                                )
                            })}
                        </Box>
                        <Box sx={{ display: 'flex', gap: '12px', mt: '12px', pt: '10px', borderTop: '1px solid', borderColor: 'divider', fontSize: 10, color: 'text.secondary', flexWrap: 'wrap' }}>
                            <Legend swatch={'primary.main'} label="Your selection" />
                            <Legend swatch="warning.light" label="Public holiday" />
                            <Legend swatch="background.paper" borderBottom="warning.main" label="Teammate on leave" />
                            <Legend swatch="action.hover" label="Weekend" />
                        </Box>
                    </Box>
                    {(errors.startDate || errors.endDate) && (
                        <FieldError id="date-error">
                            {errors.endDate?.message ?? errors.startDate?.message}
                        </FieldError>
                    )}
                </Box>

                {/* Step 3: Coverage */}
                <Box sx={sectionSx}>
                    <Box sx={sectionTitleSx}>
                        <Box component="span" sx={sectionNumSx}>3</Box>
                        Coverage
                        <Box component="span" sx={{ fontWeight: 400, color: 'text.disabled', fontSize: 12, ml: '6px' }}>(optional)</Box>
                    </Box>
                    <Box sx={sectionSubSx}>Nominate a colleague to handle urgent matters while you're away.</Box>
                    {selectedDelegate ? (
                        <Box
                            sx={{
                                display: 'flex', gap: '10px', alignItems: 'center', p: '10px 12px',
                                bgcolor: softBg('primary'), border: '1px solid', borderColor: 'primary.main', borderRadius: '8px',
                            }}
                        >
                            <DelegateAvatar name={selectedDelegate.displayName} />
                            <Box sx={{ flex: 1, minWidth: 0 }}>
                                <Box sx={{ fontSize: 12, fontWeight: 600, color: 'text.primary' }}>{selectedDelegate.displayName}</Box>
                                <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '1px' }}>
                                    {delegateSubtitle(selectedDelegate, user.departmentId)}
                                </Box>
                            </Box>
                            <Box component="button" type="button" onClick={() => setDelegatePickerOpen(true)} sx={inlineLinkSx}>Change</Box>
                            <Box component="button" type="button" onClick={clearDelegate} sx={inlineLinkSx}>Remove</Box>
                        </Box>
                    ) : (
                        <Box
                            component="button"
                            type="button"
                            onClick={() => setDelegatePickerOpen(true)}
                            sx={{
                                display: 'flex', gap: '10px', alignItems: 'center', p: '10px 12px', width: '100%',
                                bgcolor: 'action.hover', border: '1px dashed', borderColor: 'divider', borderRadius: '8px', cursor: 'pointer',
                                transition: 'all 0.15s', textAlign: 'left', fontFamily: 'inherit',
                                '&:hover': { bgcolor: softBg('primary'), borderColor: 'primary.main', borderStyle: 'solid' },
                            }}
                        >
                            <Box sx={{ width: 32, height: 32, borderRadius: '50%', bgcolor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, color: 'text.secondary' }}>+</Box>
                            <Box sx={{ flex: 1 }}>
                                <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary' }}>Choose a delegate</Box>
                                <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '1px' }}>Click to pick a teammate</Box>
                            </Box>
                            <Box component="span" sx={{ color: 'text.disabled', fontSize: 14 }}>›</Box>
                        </Box>
                    )}
                    {delegateIsAway && (
                        <Box sx={{
                            mt: '10px', p: '8px 12px', bgcolor: softBg('warning'),
                            border: '1px solid', borderColor: 'warning.main', borderRadius: '6px',
                            fontSize: 11, color: 'warning.dark',
                            display: 'flex', alignItems: 'center', gap: '6px',
                        }}>
                            <Box component="span">⚠️</Box>
                            <Box component="span">
                                {selectedDelegate?.displayName} is also off during these dates — consider nominating someone else.
                            </Box>
                        </Box>
                    )}
                </Box>

                {/* Step 4: Reason */}
                <Box sx={sectionSx}>
                    <Box sx={sectionTitleSx}>
                        <Box component="span" sx={sectionNumSx}>4</Box>
                        Reason
                        <Box component="span" sx={{ fontWeight: 400, color: 'text.disabled', fontSize: 12, ml: '6px' }}>(optional)</Box>
                    </Box>
                    <Box sx={sectionSubSx}>A brief note helps your manager approve quickly. Sensitive info stays private.</Box>
                    <Box
                        component="textarea"
                        value={reason}
                        maxLength={500}
                        onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) =>
                            setValue('reason', e.target.value, { shouldDirty: true, shouldValidate: true })
                        }
                        placeholder="e.g. Family trip to Greece — booked months ago"
                        sx={{
                            width: '100%', minHeight: 80, p: '10px 12px', fontSize: 13,
                            border: '1px solid', borderColor: errors.reason ? 'error.main' : 'divider', borderRadius: '8px', fontFamily: 'inherit',
                            resize: 'vertical', outline: 'none',
                            bgcolor: 'background.paper', color: 'text.primary', lineHeight: 1.5,
                            '&::placeholder': { color: 'text.disabled' },
                            '&:focus': { borderColor: 'primary.main' },
                        }}
                    />
                    <Box sx={{ fontSize: 11, color: 'text.disabled', textAlign: 'right', mt: '4px' }}>{reason.length} / 500</Box>
                    {errors.reason && <FieldError id="reason-error">{errors.reason.message}</FieldError>}
                    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: '6px', mt: '8px' }}>
                        <Box sx={{ fontSize: 11, color: 'text.disabled', alignSelf: 'center' }}>Quick:</Box>
                        {QUICK_REASONS.map((text, i) => (
                            <Box
                                key={text}
                                component="button"
                                type="button"
                                onClick={() => setValue('reason', text, { shouldDirty: true, shouldValidate: true })}
                                sx={{
                                    bgcolor: 'action.hover', border: '1px solid transparent', color: 'text.secondary',
                                    p: '5px 12px', borderRadius: '14px', fontSize: 11, cursor: 'pointer',
                                    fontFamily: 'inherit',
                                    '&:hover': { bgcolor: softBg('primary'), color: 'primary.main', borderColor: 'primary.main' },
                                }}
                            >
                                {QUICK_REASON_LABELS[i]}
                            </Box>
                        ))}
                    </Box>
                </Box>

                {/* Step 5: Supporting documents */}
                <Box sx={sectionSx}>
                    <Box sx={sectionTitleSx}>
                        <Box component="span" sx={sectionNumSx}>5</Box>
                        Supporting documents
                        <Box
                            component="span"
                            sx={{
                                fontWeight: 400,
                                color: isSickLeave ? 'warning.dark' : 'text.disabled',
                                fontSize: 12,
                                ml: '6px',
                            }}
                        >
                            {isSickLeave ? '(recommended for sick leave)' : '(optional)'}
                        </Box>
                    </Box>
                    <Box sx={sectionSubSx}>
                        {isSickLeave
                            ? "Doctor's note, prescription, or appointment confirmation. Attach a PDF or photo — only your manager and HR can see it."
                            : isBereavement
                                ? 'A death certificate or funeral notice helps approvals go through faster.'
                                : 'Anything that helps your manager approve: itinerary, booking confirmation, appointment letter.'}
                    </Box>

                    <input
                        ref={fileInputRef}
                        type="file"
                        accept=".pdf,.jpg,.jpeg,.png,.heic,.webp,.doc,.docx,application/pdf,image/*"
                        style={{ display: 'none' }}
                        onChange={(e) => acceptFiles(e.target.files)}
                    />

                    {!attachment && (
                        <Box
                            onClick={() => fileInputRef.current?.click()}
                            onDragOver={(e) => { e.preventDefault(); setIsDragOver(true) }}
                            onDragLeave={() => setIsDragOver(false)}
                            onDrop={(e) => {
                                e.preventDefault()
                                setIsDragOver(false)
                                acceptFiles(e.dataTransfer.files)
                            }}
                            sx={{
                                border: '2px dashed',
                                borderColor: isDragOver
                                    ? 'primary.main'
                                    : attachmentRecommended
                                        ? 'warning.main'
                                        : 'divider',
                                bgcolor: isDragOver
                                    ? softBg('primary')
                                    : attachmentRecommended
                                        ? softBg('warning')
                                        : 'action.hover',
                                borderRadius: '10px',
                                p: '20px 16px',
                                textAlign: 'center',
                                cursor: 'pointer',
                                transition: 'all 0.15s',
                                '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary') },
                            }}
                        >
                            <Box sx={{ fontSize: 28, mb: '8px' }}>📎</Box>
                            <Box sx={{ fontSize: 13, fontWeight: 500, color: 'text.primary', mb: '4px' }}>
                                Drop a file here or{' '}
                                <Box component="span" sx={{ color: 'primary.main', textDecoration: 'underline' }}>browse your device</Box>
                            </Box>
                            <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
                                PDF, JPG, PNG, HEIC, or Word · up to 10 MB
                            </Box>
                        </Box>
                    )}

                    {attachment && (
                        <Box sx={{ display: 'flex', flexDirection: 'column', gap: '8px', mt: '10px' }}>
                            <Box sx={{
                                display: 'flex', alignItems: 'center', gap: '10px',
                                p: '10px 12px', bgcolor: 'background.paper',
                                border: '1px solid', borderColor: 'divider', borderRadius: '8px',
                            }}>
                                {attachment.kind === 'img' && attachment.previewUrl ? (
                                    <Box
                                        component="img"
                                        src={attachment.previewUrl}
                                        alt=""
                                        sx={{
                                            width: 36, height: 36, borderRadius: '6px',
                                            objectFit: 'cover', flexShrink: 0,
                                            border: '1px solid', borderColor: 'divider',
                                        }}
                                    />
                                ) : (
                                    <Box sx={fileIconSx(attachment.kind)}>
                                        {attachment.kind === 'pdf' ? '📄'
                                            : attachment.kind === 'doc' ? '📝'
                                                : attachment.kind === 'img' ? '🖼️'
                                                    : '📎'}
                                    </Box>
                                )}
                                <Box sx={{ flex: 1, minWidth: 0 }}>
                                    <Box sx={{
                                        fontSize: 12, fontWeight: 500, color: 'text.primary',
                                        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                                    }}>
                                        {attachment.name}
                                    </Box>
                                    <Box sx={{
                                        fontSize: 11, color: 'text.secondary', mt: '2px',
                                        display: 'flex', alignItems: 'center', gap: '8px',
                                    }}>
                                        <Box component="span">{formatBytes(attachment.size)}</Box>
                                        <Box component="span">·</Box>
                                        <Box component="span" sx={{ color: 'success.main', display: 'inline-flex', alignItems: 'center', gap: '3px' }}>
                                            ✓ Ready to send
                                        </Box>
                                    </Box>
                                </Box>
                                <Box
                                    component="button"
                                    type="button"
                                    onClick={removeAttachment}
                                    disabled={isPending}
                                    title="Remove"
                                    sx={{
                                        bgcolor: 'transparent', border: 'none', color: 'text.disabled',
                                        cursor: 'pointer', fontSize: 14, p: '4px 8px',
                                        borderRadius: '4px', fontFamily: 'inherit',
                                        '&:hover': { bgcolor: softBg('error'), color: 'error.dark' },
                                        '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                                    }}
                                >
                                    ✕
                                </Box>
                            </Box>
                        </Box>
                    )}

                    {uploadError && (
                        <Box sx={{
                            mt: '10px', p: '8px 12px', bgcolor: softBg('error'),
                            border: '1px solid', borderColor: 'error.main', borderRadius: '6px',
                            fontSize: 11, color: 'error.dark',
                        }}>
                            {uploadError}
                        </Box>
                    )}

                    {attachmentRecommended && (
                        <Box sx={{
                            mt: '10px', p: '8px 12px', bgcolor: softBg('warning'),
                            border: '1px solid', borderColor: 'warning.main', borderRadius: '6px',
                            fontSize: 11, color: 'warning.dark',
                            display: 'flex', alignItems: 'center', gap: '6px',
                        }}>
                            <Box component="span">💡</Box>
                            <Box component="span">Tip: Sick leave is much faster to approve with a doctor's note attached.</Box>
                        </Box>
                    )}
                </Box>
            </Box>

            {/* RIGHT COLUMN — sticky summary */}
            <Box>
                <Box sx={{
                    position: 'sticky', top: 18, bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
                    borderRadius: '12px', overflow: 'hidden', height: 'fit-content',
                }}>
                    <Box sx={(theme) => ({
                        p: '16px 18px',
                        background: theme.palette.mode === 'dark'
                            ? 'linear-gradient(135deg, #1e3a8a 0%, #0f172a 100%)'
                            : 'linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)',
                        color: '#fff',
                    })}>
                        <Box sx={{ fontSize: 11, opacity: 0.9, mb: '4px', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                            {selectedType ? <>{iconForLeaveType(selectedType.name)} {selectedType.name}</> : '—'}
                        </Box>
                        <Box sx={{ fontSize: 32, fontWeight: 700, lineHeight: 1 }}>{summaryBig}</Box>
                        <Box sx={{ fontSize: 12, opacity: 0.9, mt: '6px' }}>
                            {workingDays === 0 ? 'Pick your dates to see deduction' : selectedAffectsBalance ? 'will be deducted from your balance' : 'unpaid — no deduction'}
                        </Box>
                    </Box>

                    <Box sx={{ p: '16px 18px' }}>
                        <SummaryRow l="Start date" r={formatDate(startDate)} />
                        <SummaryRow l="End date" r={formatDate(endDate)} />
                        <SummaryRow l="Working days" r={String(workingDays)} />
                        <SummaryRow l="Back at work" r={endDate ? nextWorkingDay(endDate, holidaySet) : '—'} />
                        <SummaryRow l="Days deducted" r={selectedAffectsBalance ? String(daysDeducted) : '0 (unpaid)'} />
                        <SummaryRow l="Coverage" r={selectedDelegate ? selectedDelegate.displayName : 'None'} muted={!selectedDelegate} />
                        <SummaryRow l="Attachments" r={attachment ? `📎 1 file` : 'None'} muted={!attachment} />
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', py: '8px', fontSize: 12, mt: '6px', pt: '12px', borderTop: '2px solid', borderTopColor: 'divider' }}>
                            <Box sx={{ fontWeight: 600, color: 'text.primary' }}>Balance after</Box>
                            <Box sx={{
                                fontWeight: 600,
                                color: isInsufficient ? 'error.main' : balanceAfter <= 3 && selectedAffectsBalance ? 'warning.main' : 'text.primary',
                            }}>
                                {selectedAffectsBalance ? `${balanceAfter} / ${entitlement}` : `${currentBalance} / ${entitlement}`}
                            </Box>
                        </Box>
                        <Box sx={{ height: 6, bgcolor: 'divider', borderRadius: '3px', overflow: 'hidden', mt: '6px' }}>
                            <Box sx={{
                                height: '100%', borderRadius: '3px',
                                width: `${balancePct}%`,
                                bgcolor: balancePct >= 100 ? 'error.main' : balancePct >= 80 ? 'warning.main' : 'success.main',
                            }} />
                        </Box>
                    </Box>

                    {isInsufficient && (
                        <Warning tone="error">
                            <strong>Not enough balance.</strong>{' '}
                            You'd be {Math.abs(balanceAfter)} day{Math.abs(balanceAfter) === 1 ? '' : 's'} over. Consider unpaid leave or a shorter request.
                        </Warning>
                    )}
                    {!isInsufficient && selectedAffectsBalance && balanceAfter <= 3 && workingDays > 0 && (
                        <Warning tone="warn">
                            <strong>Low balance.</strong>{' '}
                            Only {balanceAfter} {selectedType?.name.toLowerCase()} day{balanceAfter === 1 ? '' : 's'} left after this request.
                        </Warning>
                    )}
                    {conflictNames.length > 0 && (
                        <Warning tone="warn">
                            <strong>{conflictNames.join(', ')}</strong>{' '}
                            {conflictNames.length === 1 ? 'is' : 'are'} also off during these dates. Coverage may be tight.
                        </Warning>
                    )}
                    {isShortNotice && (
                        <Warning tone="info">
                            <strong>Short notice.</strong>{' '}
                            {notice === 0 ? 'Today' : notice === 1 ? 'Tomorrow' : `${notice} days from now`} — approval may take longer than usual.
                        </Warning>
                    )}
                    {workingDays > 0 && conflictNames.length === 0 && !isInsufficient && !isShortNotice && (
                        <Warning tone="good">All clear — no conflicts, good notice, plenty of balance.</Warning>
                    )}

                    {submitError && (
                        <Box sx={{ p: '10px 14px' }}>
                            <Alert severity="error" sx={{ borderRadius: '6px', fontSize: 12 }}>
                                {getApiErrorMessage(submitError, 'Failed to submit leave request. Please try again.')}
                            </Alert>
                        </Box>
                    )}

                    <Box sx={{ p: '14px 18px', bgcolor: 'action.hover', borderTop: '1px solid', borderColor: 'divider', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        <Box
                            component="button"
                            type="submit"
                            disabled={!canSubmit || isPending}
                            sx={{
                                bgcolor: 'primary.main', color: '#fff', border: 'none', p: '11px',
                                borderRadius: '8px', fontSize: 13, fontWeight: 600, cursor: 'pointer',
                                fontFamily: 'inherit', transition: 'background 0.15s',
                                display: 'inline-flex', justifyContent: 'center', alignItems: 'center', gap: '8px',
                                '&:hover:not(:disabled)': { bgcolor: 'primary.dark' },
                                '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                            }}
                        >
                            {isPending && <CircularProgress size={14} sx={{ color: '#fff' }} />}
                            {uploadMutation.isPending
                                ? 'Uploading…'
                                : submitMutation.isPending
                                    ? 'Submitting…'
                                    : canSubmit
                                        ? '✓ Submit for approval'
                                        : 'Pick dates to continue'}
                        </Box>
                        <Box
                            component="button"
                            type="button"
                            onClick={() => uiStore.navigateToMyLeave('requests')}
                            disabled={isPending}
                            sx={{
                                bgcolor: 'transparent', color: 'text.secondary', border: '1px solid', borderColor: 'divider',
                                p: '9px', borderRadius: '8px', fontSize: 12, cursor: 'pointer', fontFamily: 'inherit',
                                '&:hover': { bgcolor: 'action.hover', color: 'text.primary' },
                                '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                            }}
                        >
                            Cancel
                        </Box>
                    </Box>
                </Box>
            </Box>

            {/* Delegate picker */}
            <AppDialog open={delegatePickerOpen} onClose={closeDelegatePicker} maxWidth="xs">
                <AppDialogTitle>Choose a delegate</AppDialogTitle>
                <AppDialogContent>
                    <Box
                        component="input"
                        autoFocus
                        value={delegateSearch}
                        onChange={(e: React.ChangeEvent<HTMLInputElement>) => setDelegateSearch(e.target.value)}
                        placeholder="Search by name or job title"
                        sx={{
                            width: '100%', p: '9px 12px', fontSize: 13, fontFamily: 'inherit',
                            border: '1px solid', borderColor: 'divider', borderRadius: '8px',
                            bgcolor: 'background.paper', color: 'text.primary', outline: 'none',
                            '&::placeholder': { color: 'text.disabled' },
                            '&:focus': { borderColor: 'primary.main' },
                        }}
                    />
                    <Box sx={{ mt: '12px', maxHeight: 320, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        {visibleDelegates.length === 0 ? (
                            <Box sx={{ fontSize: 12, color: 'text.secondary', p: '18px 12px', textAlign: 'center' }}>
                                {delegateCandidates.length === 0
                                    ? 'No colleagues available to nominate.'
                                    : 'No colleagues match your search.'}
                            </Box>
                        ) : (
                            visibleDelegates.map((candidate) => {
                                const isSelected = candidate.userId === delegateId
                                const isAway = awayUserIds.has(candidate.userId)
                                return (
                                    <Box
                                        key={candidate.userId}
                                        component="button"
                                        type="button"
                                        onClick={() => chooseDelegate(candidate.userId)}
                                        sx={{
                                            display: 'flex', alignItems: 'center', gap: '10px', p: '8px 10px', width: '100%',
                                            border: '1px solid', borderColor: isSelected ? 'primary.main' : 'divider',
                                            bgcolor: isSelected ? softBg('primary') : 'background.paper',
                                            borderRadius: '8px', cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
                                            transition: 'all 0.15s',
                                            '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary') },
                                        }}
                                    >
                                        <DelegateAvatar name={candidate.displayName} />
                                        <Box sx={{ flex: 1, minWidth: 0 }}>
                                            <Box sx={{ fontSize: 12, fontWeight: 600, color: 'text.primary' }}>{candidate.displayName}</Box>
                                            <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '1px' }}>
                                                {delegateSubtitle(candidate, user.departmentId)}
                                            </Box>
                                        </Box>
                                        {isAway && (
                                            <Box sx={{
                                                fontSize: 10, px: '7px', py: '2px', borderRadius: '10px',
                                                bgcolor: softBg('warning'), color: 'warning.dark', whiteSpace: 'nowrap',
                                            }}>
                                                Away then
                                            </Box>
                                        )}
                                        {isSelected && <Box component="span" sx={{ color: 'primary.main', fontSize: 13 }}>✓</Box>}
                                    </Box>
                                )
                            })
                        )}
                    </Box>
                </AppDialogContent>
                <AppDialogActions>
                    {selectedDelegate && (
                        <Button
                            variant="outlined"
                            onClick={() => { clearDelegate(); closeDelegatePicker() }}
                            sx={cancelBtnSx}
                        >
                            Remove delegate
                        </Button>
                    )}
                    <Button variant="outlined" onClick={closeDelegatePicker} sx={cancelBtnSx}>Close</Button>
                </AppDialogActions>
            </AppDialog>
        </Box>
    )
}

/* ---------- subcomponents ---------- */

function LeaveTypeCard({
    type, selected, entitlement, used, onSelect,
}: {
    type: LeaveType
    selected: boolean
    entitlement: number
    used: number
    onSelect: () => void
}) {
    const tracksBalance = type.affectsBalance
    const remaining = Math.max(0, entitlement - used)
    const pct = entitlement > 0 ? Math.min(100, (used / entitlement) * 100) : 0
    const low = pct >= 80

    return (
        <Box
            component="button"
            type="button"
            onClick={onSelect}
            sx={{
                border: '1.5px solid',
                borderColor: selected ? 'primary.main' : 'divider',
                borderRadius: '10px', p: '14px 12px', cursor: 'pointer',
                transition: 'all 0.15s', bgcolor: selected ? softBg('primary') : 'background.paper',
                textAlign: 'left', fontFamily: 'inherit',
                '&:hover': { borderColor: 'primary.main', transform: 'translateY(-1px)' },
            }}
        >
            <Box sx={{ fontSize: 22, lineHeight: 1, mb: '8px' }}>{iconForLeaveType(type.name)}</Box>
            <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: '4px' }}>{type.name}</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '4px' }}>
                <Box component="span">{descForLeaveType(type.name)}</Box>
                {tracksBalance && entitlement > 0 && (
                    <Box component="span">
                        <Box component="strong" sx={{ color: 'text.primary', fontWeight: 700 }}>{remaining}</Box>
                        /{entitlement}
                    </Box>
                )}
            </Box>
            {tracksBalance && entitlement > 0 && (
                <Box sx={{ height: 3, bgcolor: 'divider', borderRadius: '2px', mt: '6px', overflow: 'hidden' }}>
                    <Box sx={{
                        height: '100%',
                        bgcolor: selected ? 'primary.main' : low ? 'warning.main' : 'success.main',
                        width: `${pct}%`,
                    }} />
                </Box>
            )}
        </Box>
    )
}

function DurationButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
    return (
        <Box
            component="button"
            type="button"
            onClick={onClick}
            sx={{
                p: '6px 14px', border: 'none', bgcolor: active ? 'background.paper' : 'transparent',
                fontSize: 12, color: active ? 'text.primary' : 'text.secondary', cursor: 'pointer',
                borderRadius: '6px', fontFamily: 'inherit', fontWeight: 500,
                transition: 'all 0.15s',
            }}
        >
            {children}
        </Box>
    )
}

function CalNavBtn({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
    return (
        <Box
            component="button"
            type="button"
            onClick={onClick}
            sx={{
                width: 26, height: 26, border: '1px solid', borderColor: 'divider', bgcolor: 'background.paper',
                borderRadius: '5px', cursor: 'pointer', fontSize: 12, color: 'text.secondary',
                '&:hover': { bgcolor: 'action.hover', color: 'text.primary' },
            }}
        >
            {children}
        </Box>
    )
}

function FieldError({ id, children }: { id?: string; children: React.ReactNode }) {
    return (
        <Box
            id={id}
            role="alert"
            sx={{
                mt: '10px', p: '8px 12px', bgcolor: softBg('error'),
                border: '1px solid', borderColor: 'error.main', borderRadius: '6px',
                fontSize: 11, color: 'error.dark',
                display: 'flex', alignItems: 'center', gap: '6px',
            }}
        >
            <Box component="span">⚠️</Box>
            <Box component="span">{children}</Box>
        </Box>
    )
}

function Legend({ swatch, borderBottom, label }: { swatch: string; borderBottom?: string; label: string }) {
    return (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{
                width: 10, height: 10, borderRadius: '2px', bgcolor: swatch,
                boxShadow: borderBottom ? `inset 0 -2px 0 ${borderBottom}` : 'none',
                border: swatch === '#fff' ? `1px solid ${'divider'}` : 'none',
            }} />
            {label}
        </Box>
    )
}

function initials(name: string): string {
    const parts = name.trim().split(' ').filter(Boolean)
    if (parts.length === 0) return '?'
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

function delegateSubtitle(teammate: Teammate, myDepartmentId?: number | null): string {
    const sameTeam = myDepartmentId != null && teammate.departmentId === myDepartmentId
    const title = teammate.jobTitle?.trim()
    if (title && sameTeam) return `${title} · Your team`
    if (title) return title
    return sameTeam ? 'Your team' : 'Colleague'
}

function DelegateAvatar({ name }: { name: string }) {
    return (
        <Box sx={{
            width: 32, height: 32, borderRadius: '50%', flexShrink: 0,
            bgcolor: softBg('primary'), color: 'primary.main',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: 11, fontWeight: 700,
        }}>
            {initials(name)}
        </Box>
    )
}

function SummaryRow({ l, r, muted }: { l: string; r: string; muted?: boolean }) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', py: '8px', fontSize: 12, borderBottom: '1px solid', borderBottomColor: 'divider' }}>
            <Box sx={{ color: 'text.secondary' }}>{l}</Box>
            <Box sx={{ fontWeight: muted ? 400 : 600, color: muted ? 'text.disabled' : 'text.primary' }}>{r}</Box>
        </Box>
    )
}

function fileIconSx(kind: FileKind) {
    const palette =
        kind === 'pdf' ? { bg: softBg('error'), fg: 'error.dark' } :
        kind === 'img' ? { bg: softBg('info'), fg: 'info.dark' } :
        kind === 'doc' ? { bg: softBg('info'), fg: 'info.dark' } :
                         { bg: 'action.hover', fg: 'text.secondary' }
    return {
        width: 36, height: 36, borderRadius: '6px',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: 14, flexShrink: 0,
        bgcolor: palette.bg, color: palette.fg,
    } as const
}

function Warning({ tone, children }: { tone: 'warn' | 'info' | 'good' | 'error'; children: React.ReactNode }) {
    const styles =
        tone === 'error' ? { bg: softBg('error'), fg: 'error.dark', border: 'error.main', icon: '⚠️' } :
        tone === 'warn'  ? { bg: softBg('warning'), fg: 'warning.dark', border: 'warning.main', icon: '⚠️' } :
        tone === 'info'  ? { bg: softBg('info'), fg: 'info.dark', border: 'info.main', icon: '⏰' } :
                           { bg: softBg('success'), fg: 'success.dark', border: 'success.main', icon: '✓' }
    return (
        <Box sx={{
            p: '10px 14px', fontSize: 12, display: 'flex', alignItems: 'flex-start', gap: '8px',
            bgcolor: styles.bg, color: styles.fg, borderTop: `1px solid ${styles.border}`,
        }}>
            <Box component="span">{styles.icon}</Box>
            <Box>{children}</Box>
        </Box>
    )
}

/* ---------- shared sx ---------- */

const sectionSx = {
    bgcolor: 'background.paper',
    border: '1px solid', borderColor: 'divider',
    borderRadius: '12px',
    p: '20px 22px',
    mb: '14px',
} as const

const sectionNumSx = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: 22,
    height: 22,
    borderRadius: '50%',
    bgcolor: 'primary.main',
    color: '#fff',
    fontSize: 11,
    fontWeight: 700,
    mr: '8px',
} as const

const sectionTitleSx = {
    fontSize: 14,
    fontWeight: 600,
    color: 'text.primary',
    mb: '4px',
    display: 'flex',
    alignItems: 'center',
} as const

/** Text-only button used for the inline Change / Remove affordances. */
const inlineLinkSx = {
    bgcolor: 'transparent',
    border: 'none',
    p: '2px 4px',
    fontSize: 11,
    fontWeight: 600,
    fontFamily: 'inherit',
    color: 'primary.main',
    cursor: 'pointer',
    borderRadius: '4px',
    '&:hover': { textDecoration: 'underline' },
} as const

const sectionSubSx = {
    fontSize: 12,
    color: 'text.secondary',
    mb: '14px',
    pl: '30px',
} as const

function calCellSx({ weekend, holiday, teammate, today, rangeStart, rangeEnd, inRange, otherMonth }: {
    weekend?: boolean; holiday?: boolean; teammate?: boolean; today?: boolean
    rangeStart?: boolean; rangeEnd?: boolean; inRange?: boolean; otherMonth?: boolean
}) {
    const isEdge = rangeStart || rangeEnd
    let bg: SxColor | undefined
    let color: SxColor = 'text.primary'
    let borderRadius = '6px'
    if (otherMonth) color = 'divider'
    else if (weekend) color = 'text.disabled'
    if (holiday && !isEdge) {
        bg = softBg('warning')
        color = 'warning.dark'
    }
    if (isEdge) {
        bg = 'primary.main'
        color = '#fff'
        if (rangeStart && !rangeEnd) borderRadius = '6px 0 0 6px'
        if (rangeEnd && !rangeStart) borderRadius = '0 6px 6px 0'
    } else if (inRange) {
        bg = softBg('info')
        color = 'info.dark'
        borderRadius = '0'
    }

    const disabled = weekend || holiday || otherMonth
    return {
        aspectRatio: '1',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: 12,
        color,
        cursor: disabled ? 'not-allowed' : 'pointer',
        borderRadius,
        position: 'relative',
        userSelect: 'none',
        bgcolor: bg,
        fontWeight: today || isEdge ? 700 : 400,
        boxShadow: teammate && !isEdge ? (theme: Theme) => `inset 0 -2px 0 ${theme.palette.warning.main}` : 'none',
        '&:hover': disabled ? {} : { bgcolor: isEdge ? 'primary.main' : inRange ? softBg('info') : softBg('primary') },
        '&::after': holiday && !isEdge ? {
            content: '""', position: 'absolute', top: 3, right: 3,
            width: 5, height: 5, borderRadius: '50%', bgcolor: 'warning.main',
        } : today ? {
            content: '""', position: 'absolute', bottom: 3, left: '50%',
            transform: 'translateX(-50%)', width: 4, height: 4, borderRadius: '50%',
            bgcolor: isEdge ? '#fff' : 'primary.main',
        } : undefined,
    } as const
}

export default ApplyLeavePage
