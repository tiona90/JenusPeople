import apiClient from './client'
import type { LeaveStatusHistory } from '../types'
import { toPaged, type PageParams, type Paged } from './pagination'

// Zero-arg so it stays safe to pass directly as a React Query queryFn.
export async function getLeaveStatusHistories() {
    const response = await apiClient.get<LeaveStatusHistory[]>('/leavestatushistories')
    return response.data
}

/**
 * The newest 50 changes — what the employee's bell reads (it shows eight and
 * counts the unread ones from the last week). The endpoint pages newest first in
 * SQL. No parameters on purpose: it is passed bare as a queryFn.
 */
export async function getRecentLeaveStatusHistories() {
    const response = await apiClient.get<LeaveStatusHistory[]>('/leavestatushistories', { params: { page: 1, pageSize: 50 } })
    return response.data
}

// Paged variant: returns items + total (read from the X-Total-Count header).
export async function getLeaveStatusHistoriesPaged(params: PageParams): Promise<Paged<LeaveStatusHistory>> {
    const response = await apiClient.get<LeaveStatusHistory[]>('/leavestatushistories', { params })
    return toPaged(response, params)
}
