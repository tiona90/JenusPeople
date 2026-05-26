import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import {
    approveTimesheet,
    createTimesheet,
    deleteTimesheet,
    getMyTimesheets,
    getTimesheet,
    getTimesheets,
    rejectTimesheet,
    submitTimesheet,
    updateTimesheet,
} from '../api/timesheets'
import {
    createTimesheetEntry,
    deleteTimesheetEntry,
    updateTimesheetEntry,
} from '../api/timesheet-entries'
import { getTimesheetStatusHistories } from '../api/timesheet-status-histories'
import type { Timesheet } from '../types/timesheet'
import type { TimesheetEntry } from '../types/timesheet-entry'
import type { TimesheetStatusHistory } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useTimesheets(options?: QueryOpts<Timesheet[]>) {
    return useQuery({
        queryKey: queryKeys.timesheets,
        queryFn: getTimesheets,
        ...options,
    })
}

export function useMyTimesheets(options?: QueryOpts<Timesheet[]>) {
    return useQuery({
        queryKey: queryKeys.myTimesheets,
        queryFn: getMyTimesheets,
        ...options,
    })
}

export function useTimesheet(id: string | undefined | null, options?: QueryOpts<Timesheet>) {
    return useQuery({
        queryKey: queryKeys.timesheetDetail(id),
        queryFn: () => getTimesheet(id as string),
        enabled: !!id,
        ...options,
    })
}

export function useTimesheetStatusHistories(options?: QueryOpts<TimesheetStatusHistory[]>) {
    return useQuery({
        queryKey: queryKeys.timesheetStatusHistories,
        queryFn: getTimesheetStatusHistories,
        ...options,
    })
}

function useInvalidateTimesheets() {
    const qc = useQueryClient()
    return (timesheetId?: string) => {
        void qc.invalidateQueries({ queryKey: queryKeys.timesheets })
        void qc.invalidateQueries({ queryKey: queryKeys.myTimesheets })
        void qc.invalidateQueries({ queryKey: queryKeys.timesheetStatusHistories })
        if (timesheetId) {
            void qc.invalidateQueries({ queryKey: queryKeys.timesheetDetail(timesheetId) })
        }
    }
}

export function useCreateTimesheet() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (data: { periodStart: string; periodEnd: string }) => createTimesheet(data),
        onSuccess: (created) => invalidate(created?.id),
    })
}

export function useUpdateTimesheet() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (vars: { id: string; data: Partial<Timesheet> }) => updateTimesheet(vars.id, vars.data),
        onSuccess: (_data, vars) => invalidate(vars.id),
    })
}

export function useDeleteTimesheet() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (id: string) => deleteTimesheet(id),
        onSuccess: (_data, id) => invalidate(id),
    })
}

export function useSubmitTimesheet() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (id: string) => submitTimesheet(id),
        onSuccess: (_data, id) => invalidate(id),
    })
}

export function useApproveTimesheet() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (id: string) => approveTimesheet(id),
        onSuccess: (_data, id) => invalidate(id),
    })
}

export function useRejectTimesheet() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (id: string) => rejectTimesheet(id),
        onSuccess: (_data, id) => invalidate(id),
    })
}

export function useCreateTimesheetEntry() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (vars: { timesheetId: string; entry: Omit<TimesheetEntry, 'id'> }) =>
            createTimesheetEntry(vars.timesheetId, vars.entry),
        onSuccess: (_data, vars) => invalidate(vars.timesheetId),
    })
}

export function useUpdateTimesheetEntry() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (vars: { timesheetId: string; entryId: string; entry: TimesheetEntry }) =>
            updateTimesheetEntry(vars.timesheetId, vars.entryId, vars.entry),
        onSuccess: (_data, vars) => invalidate(vars.timesheetId),
    })
}

export function useDeleteTimesheetEntry() {
    const invalidate = useInvalidateTimesheets()
    return useMutation({
        mutationFn: (vars: { timesheetId: string; entryId: string }) =>
            deleteTimesheetEntry(vars.timesheetId, vars.entryId),
        onSuccess: (_data, vars) => invalidate(vars.timesheetId),
    })
}
