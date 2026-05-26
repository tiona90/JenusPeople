import { useQuery, type UseQueryOptions } from '@tanstack/react-query'
import { getLeaveStatusHistories } from '../api/leave-status-histories'
import type { LeaveStatusHistory } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useLeaveStatusHistories(options?: QueryOpts<LeaveStatusHistory[]>) {
    return useQuery({
        queryKey: queryKeys.leaveStatusHistories,
        queryFn: getLeaveStatusHistories,
        ...options,
    })
}
