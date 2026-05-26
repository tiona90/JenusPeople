import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import { getAppSettings, updateAppSettings } from '../api/settings'
import type { AppSettings } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useAppSettings(options?: QueryOpts<AppSettings>) {
    return useQuery({
        queryKey: queryKeys.appSettings,
        queryFn: getAppSettings,
        ...options,
    })
}

export function useUpdateAppSettings() {
    const qc = useQueryClient()
    return useMutation({
        mutationFn: (data: AppSettings) => updateAppSettings(data),
        onSuccess: (data) => {
            qc.setQueryData(queryKeys.appSettings, data)
        },
    })
}
