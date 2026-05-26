import { useMemo, useState } from 'react'
import { observer } from 'mobx-react-lite'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import FormControl from '@mui/material/FormControl'
import MenuItem from '@mui/material/MenuItem'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Add as AddIcon, Assignment as AssignmentIcon } from '@mui/icons-material'
import { useAnnualLeaves, useLeaveTypes } from '../../lib/hooks'
import { useStore } from '../../lib/mobx'
import type { AnnualLeave, AnnualLeaveStatus, UserInfo } from '../../lib/types'
import AnnualLeaveCard from './AnnualLeaveCard'
import AnnualLeaveForm from './AnnualLeaveForm'

type AnnualLeaveListProps = {
    user: UserInfo
    filterPredicate?: (leave: AnnualLeave) => boolean
    showCreateButton?: boolean
    emptyMessage?: string
    isAdmin?: boolean
    showYearFilter?: boolean
    selectedYear?: number | 'all'
    onSelectedYearChange?: (year: number | 'all') => void
    yearOptions?: number[]
    title?: string
    createButtonLabel?: string
}

const AnnualLeaveList = observer(function AnnualLeaveList({
    user,
    filterPredicate,
    showCreateButton = true,
    emptyMessage = 'No leave requests found.',
    isAdmin = false,
    showYearFilter = true,
    selectedYear: controlledSelectedYear,
    onSelectedYearChange,
    yearOptions,
    title = 'Requests',
    createButtonLabel = isAdmin ? 'Assign Leave to User' : 'New Leave Request',
}: AnnualLeaveListProps) {
    const { uiStore } = useStore()
    const [internalSelectedYear, setInternalSelectedYear] = useState<number | 'all'>(new Date().getFullYear())
    const [sortOrder, setSortOrder] = useState<'newest' | 'oldest'>('newest')
    const [statusFilter, setStatusFilter] = useState<AnnualLeaveStatus | 'all'>('all')
    const [leaveTypeFilter, setLeaveTypeFilter] = useState('all')
    const selectedYear = controlledSelectedYear ?? internalSelectedYear
    const setSelectedYear = onSelectedYearChange ?? setInternalSelectedYear

    const { data: leaves, isLoading, isError } = useAnnualLeaves()
    const { data: leaveTypes = [] } = useLeaveTypes()

    const leaveTypeNameById = useMemo(
        () => new Map(leaveTypes.map((leaveType) => [leaveType.id, leaveType.name])),
        [leaveTypes]
    )

    const availableYears = useMemo(
        () => (yearOptions && yearOptions.length > 0
            ? [...yearOptions].sort((left, right) => right - left)
            : Array.from(new Set((leaves ?? []).map((leave) => new Date(leave.startDate).getFullYear())))
                .sort()
                .reverse()),
        [leaves, yearOptions]
    )

    const leavesByYear = useMemo(
        () => selectedYear === 'all'
            ? (leaves ?? [])
            : (leaves ?? []).filter((leave) => new Date(leave.startDate).getFullYear() === selectedYear),
        [leaves, selectedYear]
    )

    const scopedLeaves = useMemo(
        () => filterPredicate ? leavesByYear.filter(filterPredicate) : leavesByYear,
        [filterPredicate, leavesByYear]
    )

    const availableStatuses = useMemo(
        () => Array.from(new Set(scopedLeaves.map((leave) => leave.status))).sort((left, right) => left.localeCompare(right)),
        [scopedLeaves]
    )

    const availableLeaveTypes = useMemo(
        () => Array.from(new Set(scopedLeaves.map((leave) => leave.leaveTypeId != null
            ? (leaveTypeNameById.get(leave.leaveTypeId) ?? 'Annual Leave')
            : 'Annual Leave'))).sort((left, right) => left.localeCompare(right)),
        [leaveTypeNameById, scopedLeaves]
    )

    const filteredLeaves = useMemo(
        () => scopedLeaves.filter((leave) => {
            if (statusFilter !== 'all' && leave.status !== statusFilter) {
                return false
            }

            const leaveTypeLabel = leave.leaveTypeId != null
                ? (leaveTypeNameById.get(leave.leaveTypeId) ?? 'Annual Leave')
                : 'Annual Leave'

            if (leaveTypeFilter !== 'all' && leaveTypeLabel !== leaveTypeFilter) {
                return false
            }

            return true
        }),
        [leaveTypeFilter, leaveTypeNameById, scopedLeaves, statusFilter]
    )

    const visibleLeaves = useMemo(
        () => [...filteredLeaves].sort((left, right) => {
            const newestFirstStartDiff = new Date(right.startDate).getTime() - new Date(left.startDate).getTime()
            if (newestFirstStartDiff !== 0) {
                return sortOrder === 'oldest' ? -newestFirstStartDiff : newestFirstStartDiff
            }

            const newestFirstCreatedDiff = new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime()
            return sortOrder === 'oldest' ? -newestFirstCreatedDiff : newestFirstCreatedDiff
        }),
        [filteredLeaves, sortOrder]
    )

    const hasActiveFilters = statusFilter !== 'all' || leaveTypeFilter !== 'all'

    if (isLoading) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
                <CircularProgress />
            </Box>
        )
    }

    if (isError) {
        return <Alert severity="error">Failed to load leave requests.</Alert>
    }

    return (
        <Stack spacing={2}>
            <Typography variant="h6" fontWeight={800} sx={{ mb: 0.25, display: 'flex', alignItems: 'center', gap: 1 }}>
                <AssignmentIcon fontSize="small" />
                {title}
            </Typography>

            {scopedLeaves.length > 0 && (
                <Stack
                    direction={{ xs: 'column', md: 'row' }}
                    spacing={1}
                    useFlexGap
                    sx={{ mb: 0.5 }}
                >
                    {showYearFilter && (
                        <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 140 } }}>
                            <Select
                                value={selectedYear}
                                onChange={(e) => {
                                    const value = e.target.value
                                    setSelectedYear(value === 'all' ? 'all' : Number(value))
                                }}
                                sx={{ borderRadius: 999, backgroundColor: 'rgba(248,250,252,0.9)' }}
                            >
                                <MenuItem value="all">All years</MenuItem>
                                {availableYears.map((year) => (
                                    <MenuItem key={year} value={year}>
                                        {year}
                                    </MenuItem>
                                ))}
                            </Select>
                        </FormControl>
                    )}

                    <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 150 } }}>
                        <Select
                            value={statusFilter}
                            onChange={(e) => setStatusFilter(e.target.value as AnnualLeaveStatus | 'all')}
                            sx={{ borderRadius: 999, backgroundColor: 'rgba(248,250,252,0.9)' }}
                        >
                            <MenuItem value="all">All statuses</MenuItem>
                            {availableStatuses.map((status) => (
                                <MenuItem key={status} value={status}>{status}</MenuItem>
                            ))}
                        </Select>
                    </FormControl>

                    <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 160 } }}>
                        <Select
                            value={leaveTypeFilter}
                            onChange={(e) => setLeaveTypeFilter(String(e.target.value))}
                            sx={{ borderRadius: 999, backgroundColor: 'rgba(248,250,252,0.9)' }}
                        >
                            <MenuItem value="all">All leave types</MenuItem>
                            {availableLeaveTypes.map((leaveType) => (
                                <MenuItem key={leaveType} value={leaveType}>{leaveType}</MenuItem>
                            ))}
                        </Select>
                    </FormControl>

                    <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 145 } }}>
                        <Select
                            value={sortOrder}
                            onChange={(e) => setSortOrder(e.target.value as 'newest' | 'oldest')}
                            sx={{ borderRadius: 999, backgroundColor: 'rgba(248,250,252,0.9)' }}
                        >
                            <MenuItem value="newest">Newest first</MenuItem>
                            <MenuItem value="oldest">Oldest first</MenuItem>
                        </Select>
                    </FormControl>

                    <Chip
                        size="small"
                        variant="outlined"
                        color="primary"
                        label={`${visibleLeaves.length} request${visibleLeaves.length === 1 ? '' : 's'}`}
                        sx={{ alignSelf: { xs: 'flex-start', md: 'center' } }}
                    />
                </Stack>
            )}

            {showCreateButton && (
                <Box sx={{ pb: 0.5 }}>
                    <Button
                        variant="contained"
                        startIcon={<AddIcon />}
                        onClick={() => uiStore.openCreateDrawer()}
                        sx={{
                            borderRadius: 999,
                            px: 2.2,
                            py: 1,
                            textTransform: 'none',
                            fontWeight: 700,
                        }}
                    >
                        {createButtonLabel}
                    </Button>
                </Box>
            )}

            {visibleLeaves.length === 0 ? (
                <Box
                    sx={{
                        py: 4.5,
                        textAlign: 'center',
                        border: '1px dashed',
                        borderColor: 'divider',
                        borderRadius: 2.5,
                    }}
                >
                    <Typography color="text.secondary">
                        {hasActiveFilters ? 'No leave requests match the selected filters.' : emptyMessage}
                    </Typography>
                </Box>
            ) : (
                visibleLeaves.map((leave) => (
                    <AnnualLeaveCard key={leave.id} leave={leave} user={user} />
                ))
            )}

            <AnnualLeaveForm open={uiStore.isCreateDrawerOpen} onClose={() => uiStore.closeCreateDrawer()} isAdmin={isAdmin} />
        </Stack>
    )
})

export default AnnualLeaveList
