import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import {
    checkIn as apiCheckIn,
    checkOut as apiCheckOut,
    endBreak as apiEndBreak,
    getAttendanceHistory,
    getAttendanceToday,
    getCompanyAttendance,
    getTeamAttendance,
    startBreak as apiStartBreak,
} from '../api/attendance'
import type { AttendanceHistoryDay, AttendanceToday, CompanyAttendance, TeamAttendance } from '../types/attendance'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

// Re-exported for callers that prefer the constant directly (e.g. setQueryData).
export const attendanceQueryKey = queryKeys.attendanceToday

export function useAttendanceToday(enabled = true) {
    return useQuery({
        queryKey: queryKeys.attendanceToday,
        queryFn: getAttendanceToday,
        enabled,
        refetchInterval: 60_000,
        refetchIntervalInBackground: true,
        staleTime: 30_000,
    })
}

export function useAttendanceHistory(days = 30, options?: QueryOpts<AttendanceHistoryDay[]>) {
    return useQuery({
        queryKey: queryKeys.attendanceHistory(days),
        queryFn: () => getAttendanceHistory(days),
        ...options,
    })
}

export function useTeamAttendance(options?: QueryOpts<TeamAttendance>) {
    return useQuery({
        queryKey: queryKeys.teamAttendance,
        queryFn: getTeamAttendance,
        refetchInterval: 30_000,
        ...options,
    })
}

export function useCompanyAttendance(options?: QueryOpts<CompanyAttendance>) {
    return useQuery({
        queryKey: queryKeys.companyAttendance,
        queryFn: getCompanyAttendance,
        refetchInterval: 30_000,
        ...options,
    })
}

type AttendanceAction = 'check-in' | 'check-out' | 'break-start' | 'break-end'

function applyOptimisticAction(
    prev: AttendanceToday | undefined,
    action: AttendanceAction,
): AttendanceToday | undefined {
    const nowMs = Date.now()
    const nowIso = new Date(nowMs).toISOString()
    const todayIso = nowIso.slice(0, 10)

    if (action === 'check-in') {
        return prev
            ? { ...prev, status: 'in', checkInAt: prev.checkInAt ?? nowIso, onBreakSince: null }
            : {
                date: todayIso,
                status: 'in',
                checkInAt: nowIso,
                checkOutAt: null,
                onBreakSince: null,
                totalBreakMinutes: 0,
                workedMinutes: 0,
                events: [],
            }
    }
    if (!prev) return prev

    if (action === 'check-out') {
        // If currently on break, close it out in the optimistic snapshot too.
        const closedBreakMinutes = prev.status === 'break' && prev.onBreakSince
            ? Math.max(0, Math.floor((nowMs - new Date(prev.onBreakSince).getTime()) / 60_000))
            : 0
        return {
            ...prev,
            status: 'done',
            checkOutAt: nowIso,
            onBreakSince: null,
            totalBreakMinutes: prev.totalBreakMinutes + closedBreakMinutes,
        }
    }
    if (action === 'break-start') {
        return { ...prev, status: 'break', onBreakSince: nowIso }
    }
    // break-end
    const breakMinutes = prev.onBreakSince
        ? Math.max(0, Math.floor((nowMs - new Date(prev.onBreakSince).getTime()) / 60_000))
        : 0
    return {
        ...prev,
        status: 'in',
        onBreakSince: null,
        totalBreakMinutes: prev.totalBreakMinutes + breakMinutes,
    }
}

export function useAttendanceActions() {
    const qc = useQueryClient()

    function useAttendanceMutation(action: AttendanceAction, mutationFn: () => Promise<AttendanceToday>) {
        return useMutation<AttendanceToday, Error, void, { previous: AttendanceToday | undefined }>({
            mutationFn,
            onMutate: async () => {
                // Block any in-flight fetch from clobbering the optimistic snapshot.
                await qc.cancelQueries({ queryKey: queryKeys.attendanceToday })
                const previous = qc.getQueryData<AttendanceToday>(queryKeys.attendanceToday)
                qc.setQueryData<AttendanceToday | undefined>(
                    queryKeys.attendanceToday,
                    (curr) => applyOptimisticAction(curr ?? previous, action),
                )
                return { previous }
            },
            onError: (_err, _vars, context) => {
                qc.setQueryData(queryKeys.attendanceToday, context?.previous)
            },
            onSuccess: (data) => {
                qc.setQueryData(queryKeys.attendanceToday, data)
            },
            onSettled: () => {
                void qc.invalidateQueries({ queryKey: ['attendance', 'history'] })
                void qc.invalidateQueries({ queryKey: queryKeys.teamAttendance })
                void qc.invalidateQueries({ queryKey: queryKeys.companyAttendance })
            },
        })
    }

    const checkIn = useAttendanceMutation('check-in', apiCheckIn)
    const checkOut = useAttendanceMutation('check-out', apiCheckOut)
    const startBreak = useAttendanceMutation('break-start', apiStartBreak)
    const endBreak = useAttendanceMutation('break-end', apiEndBreak)
    const anyPending = checkIn.isPending || checkOut.isPending || startBreak.isPending || endBreak.isPending
    return { checkIn, checkOut, startBreak, endBreak, anyPending }
}

export function useLiveElapsedMinutes(today: AttendanceToday | undefined): number {
    const [now, setNow] = useState<number>(() => Date.now())

    useEffect(() => {
        if (!today || today.status === 'out' || today.status === 'done') return
        const id = window.setInterval(() => setNow(Date.now()), 30_000)
        return () => window.clearInterval(id)
    }, [today])

    if (!today || !today.checkInAt) return 0
    if (today.status === 'done') return today.workedMinutes

    const checkInMs = new Date(today.checkInAt).getTime()
    const endMs = today.checkOutAt ? new Date(today.checkOutAt).getTime() : now
    const totalMs = endMs - checkInMs
    const closedBreakMs = today.totalBreakMinutes * 60_000
    const openBreakMs = today.onBreakSince ? Math.max(0, now - new Date(today.onBreakSince).getTime()) : 0
    return Math.max(0, Math.floor((totalMs - closedBreakMs - openBreakMs) / 60_000))
}

export function formatElapsed(minutes: number) {
    const h = Math.floor(minutes / 60)
    const m = minutes % 60
    return `${h}h ${m.toString().padStart(2, '0')}m`
}

export function formatTime(iso: string) {
    return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
}

export function formatTime12(iso: string) {
    return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', hour12: true })
}
