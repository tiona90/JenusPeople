import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import {
    createAnnualLeave,
    deleteAnnualLeave,
    editAnnualLeave,
    getAnnualLeaveDetails,
    getAnnualLeaves,
    getTeamAwayThisWeekCount,
    updateLeaveStatus,
    uploadLeaveEvidence,
} from '../api/annual-leaves'
import type { AnnualLeave, CreateAnnualLeaveRequest, EditAnnualLeaveRequest } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useAnnualLeaves(options?: QueryOpts<AnnualLeave[]>) {
    return useQuery({
        queryKey: queryKeys.annualLeaves,
        queryFn: getAnnualLeaves,
        ...options,
    })
}

export function useAnnualLeaveDetails(id: string | undefined | null, options?: QueryOpts<AnnualLeave>) {
    return useQuery({
        queryKey: queryKeys.annualLeaveDetail(id),
        queryFn: () => getAnnualLeaveDetails(id as string),
        enabled: !!id,
        ...options,
    })
}

export function useTeamAwayThisWeekCount(options?: QueryOpts<number>) {
    return useQuery({
        queryKey: queryKeys.teamAwayThisWeekCount,
        queryFn: getTeamAwayThisWeekCount,
        ...options,
    })
}

function useInvalidateAnnualLeaves() {
    const qc = useQueryClient()
    return () => {
        void qc.invalidateQueries({ queryKey: queryKeys.annualLeaves })
        void qc.invalidateQueries({ queryKey: queryKeys.leaveStatusHistories })
        void qc.invalidateQueries({ queryKey: queryKeys.teamAwayThisWeekCount })
    }
}

export function useCreateAnnualLeave() {
    const invalidate = useInvalidateAnnualLeaves()
    return useMutation({
        mutationFn: (request: CreateAnnualLeaveRequest) => createAnnualLeave(request),
        onSuccess: () => invalidate(),
    })
}

export function useEditAnnualLeave() {
    const invalidate = useInvalidateAnnualLeaves()
    return useMutation({
        mutationFn: (request: EditAnnualLeaveRequest) => editAnnualLeave(request),
        onSuccess: () => invalidate(),
    })
}

export function useDeleteAnnualLeave() {
    const invalidate = useInvalidateAnnualLeaves()
    return useMutation({
        mutationFn: (id: string) => deleteAnnualLeave(id),
        onSuccess: () => invalidate(),
    })
}

export function useUpdateLeaveStatus() {
    const invalidate = useInvalidateAnnualLeaves()
    return useMutation({
        mutationFn: (vars: { id: string; status: 'Approved' | 'Rejected' | 'Cancelled'; comment?: string }) =>
            updateLeaveStatus(vars.id, vars.status, vars.comment),
        onSuccess: () => invalidate(),
    })
}

export function useUploadLeaveEvidence() {
    return useMutation({
        mutationFn: (file: File) => uploadLeaveEvidence(file),
    })
}
