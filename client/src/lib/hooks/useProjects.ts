import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query'
import { createProject, deleteProject, getProjects, updateProject } from '../api/projects'
import type { Project, UpsertProjectRequest } from '../types/project'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

export function useProjects(options?: QueryOpts<Project[]>) {
    return useQuery({
        queryKey: queryKeys.projects,
        queryFn: getProjects,
        ...options,
    })
}

function useInvalidateProjects() {
    const qc = useQueryClient()
    return () => qc.invalidateQueries({ queryKey: queryKeys.projects })
}

export function useCreateProject() {
    const invalidate = useInvalidateProjects()
    return useMutation({
        mutationFn: (request: UpsertProjectRequest) => createProject(request),
        onSuccess: () => { void invalidate() },
    })
}

export function useUpdateProject() {
    const invalidate = useInvalidateProjects()
    return useMutation({
        mutationFn: (vars: { id: number; payload: UpsertProjectRequest }) => updateProject(vars.id, vars.payload),
        onSuccess: () => { void invalidate() },
    })
}

export function useDeleteProject() {
    const invalidate = useInvalidateProjects()
    return useMutation({
        mutationFn: (id: number) => deleteProject(id),
        onSuccess: () => { void invalidate() },
    })
}
