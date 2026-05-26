import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import {
    createDepartment,
    deleteDepartment,
    getDepartments,
    updateDepartment,
    type UpsertDepartmentRequest,
} from '../api/departments'
import type { Department } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useDepartments(options?: QueryOpts<Department[]>) {
    return useQuery({
        queryKey: queryKeys.departments,
        queryFn: getDepartments,
        ...options,
    })
}

function useInvalidateDepartments() {
    const qc = useQueryClient()
    return () => qc.invalidateQueries({ queryKey: queryKeys.departments })
}

export function useCreateDepartment() {
    const invalidate = useInvalidateDepartments()
    return useMutation({
        mutationFn: (request: UpsertDepartmentRequest) => createDepartment(request),
        onSuccess: () => { void invalidate() },
    })
}

export function useUpdateDepartment() {
    const invalidate = useInvalidateDepartments()
    return useMutation({
        mutationFn: (vars: { id: number; payload: UpsertDepartmentRequest }) => updateDepartment(vars.id, vars.payload),
        onSuccess: () => { void invalidate() },
    })
}

export function useDeleteDepartment() {
    const invalidate = useInvalidateDepartments()
    return useMutation({
        mutationFn: (id: number) => deleteDepartment(id),
        onSuccess: () => { void invalidate() },
    })
}
