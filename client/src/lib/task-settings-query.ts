import { useQuery } from '@tanstack/react-query'
import { WORK_TASK_SETTINGS_KEY, getWorkTaskSettings } from './api'
import type { WorkTaskSettings } from './api'
import { DEFAULT_TASK_SETTINGS } from './task-settings'

/**
 * The rules every task surface reads. Never undefined: while loading, and against an
 * older API with no endpoint, it is today's behaviour — so nothing is held back or
 * hidden that the server would not hold back or hide.
 */
export function useWorkTaskSettings(): WorkTaskSettings {
    const { data } = useQuery({
        queryKey: WORK_TASK_SETTINGS_KEY ?? ['work-tasks', 'settings'],
        queryFn: getWorkTaskSettings,
        retry: false,
        staleTime: 60_000,
    })
    return data ?? DEFAULT_TASK_SETTINGS
}
