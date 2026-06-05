import apiClient from './client'
import type { AppSettings } from '../types'

export async function getAppSettings(): Promise<AppSettings> {
    const res = await apiClient.get<AppSettings>('/settings')
    return res.data
}

export async function updateAppSettings(data: AppSettings): Promise<AppSettings> {
    const res = await apiClient.put<AppSettings>('/settings', data)
    return res.data
}

// Danger zone: restore all reminders to factory defaults. Returns the updated settings.
export async function resetReminders(): Promise<AppSettings> {
    const res = await apiClient.post<AppSettings>('/settings/reset-reminders')
    return res.data
}

// Danger zone: delete leave & timesheet approval-history from the past 30 days.
// Returns the number of records removed.
export async function clearApprovalHistory(): Promise<number> {
    const res = await apiClient.post<number>('/settings/clear-approval-history')
    return res.data
}
