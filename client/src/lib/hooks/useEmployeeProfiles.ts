import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import { getEmployeeProfiles, updateEmployeeProfile } from '../api/employee-profiles'
import type { EditEmployeeProfileRequest, EmployeeProfile } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useEmployeeProfiles(options?: QueryOpts<EmployeeProfile[]>) {
    return useQuery({
        queryKey: queryKeys.employeeProfiles,
        queryFn: getEmployeeProfiles,
        ...options,
    })
}

export function useUpdateEmployeeProfile() {
    const qc = useQueryClient()
    return useMutation({
        mutationFn: (request: EditEmployeeProfileRequest) => updateEmployeeProfile(request),
        onSuccess: () => {
            void qc.invalidateQueries({ queryKey: queryKeys.employeeProfiles })
        },
    })
}
