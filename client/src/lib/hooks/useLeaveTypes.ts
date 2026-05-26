import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import {
    createLeaveType,
    deleteLeaveType,
    getLeaveTypes,
    updateLeaveType,
    type UpsertLeaveTypeRequest,
} from '../api/leave-types'
import type { LeaveType } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useLeaveTypes(options?: QueryOpts<LeaveType[]>) {
    return useQuery({
        queryKey: queryKeys.leaveTypes,
        queryFn: getLeaveTypes,
        ...options,
    })
}

function useInvalidateLeaveTypes() {
    const qc = useQueryClient()
    return () => qc.invalidateQueries({ queryKey: queryKeys.leaveTypes })
}

export function useCreateLeaveType() {
    const invalidate = useInvalidateLeaveTypes()
    return useMutation({
        mutationFn: (request: UpsertLeaveTypeRequest) => createLeaveType(request),
        onSuccess: () => { void invalidate() },
    })
}

export function useUpdateLeaveType() {
    const invalidate = useInvalidateLeaveTypes()
    return useMutation({
        mutationFn: (vars: { id: number; payload: UpsertLeaveTypeRequest }) => updateLeaveType(vars.id, vars.payload),
        onSuccess: () => { void invalidate() },
    })
}

export function useDeleteLeaveType() {
    const invalidate = useInvalidateLeaveTypes()
    return useMutation({
        mutationFn: (id: number) => deleteLeaveType(id),
        onSuccess: () => { void invalidate() },
    })
}
