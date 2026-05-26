import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import {
    createAdminUser,
    deleteAdminUser,
    getAdminUsers,
    setAdminUserRoles,
    updateAdminUser,
} from '../api/admin-users'
import type {
    AdminCreateUserRequest,
    AdminSetUserRolesRequest,
    AdminUpdateUserRequest,
    AdminUser,
} from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useAdminUsers(options?: QueryOpts<AdminUser[]>) {
    return useQuery({
        queryKey: queryKeys.adminUsers,
        queryFn: getAdminUsers,
        ...options,
    })
}

function useInvalidateAdminUsers() {
    const qc = useQueryClient()
    return () => {
        void qc.invalidateQueries({ queryKey: queryKeys.adminUsers })
        void qc.invalidateQueries({ queryKey: queryKeys.employeeProfiles })
    }
}

export function useCreateAdminUser() {
    const invalidate = useInvalidateAdminUsers()
    return useMutation({
        mutationFn: (request: AdminCreateUserRequest) => createAdminUser(request),
        onSuccess: () => invalidate(),
    })
}

export function useUpdateAdminUser() {
    const invalidate = useInvalidateAdminUsers()
    return useMutation({
        mutationFn: (vars: { id: string; payload: AdminUpdateUserRequest }) => updateAdminUser(vars.id, vars.payload),
        onSuccess: () => invalidate(),
    })
}

export function useSetAdminUserRoles() {
    const invalidate = useInvalidateAdminUsers()
    return useMutation({
        mutationFn: (vars: { id: string; payload: AdminSetUserRolesRequest }) => setAdminUserRoles(vars.id, vars.payload),
        onSuccess: () => invalidate(),
    })
}

export function useDeleteAdminUser() {
    const invalidate = useInvalidateAdminUsers()
    return useMutation({
        mutationFn: (id: string) => deleteAdminUser(id),
        onSuccess: () => invalidate(),
    })
}
