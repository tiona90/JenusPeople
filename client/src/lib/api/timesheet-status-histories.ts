import apiClient from './client'
import type { TimesheetStatusHistory } from '../types'
import { toPaged, type PageParams, type Paged } from './pagination'

// Zero-arg so it stays safe to pass directly as a React Query queryFn.
export async function getTimesheetStatusHistories() {
    const response = await apiClient.get<TimesheetStatusHistory[]>('/timesheetstatushistories')
    return response.data
}

// Paged variant: returns items + total (read from the X-Total-Count header).
export async function getTimesheetStatusHistoriesPaged(params: PageParams): Promise<Paged<TimesheetStatusHistory>> {
    const response = await apiClient.get<TimesheetStatusHistory[]>('/timesheetstatushistories', { params })
    return toPaged(response, params)
}
