import apiClient from './client'
import type { AttendanceHistoryDay, AttendanceToday, CompanyAttendance, TeamAttendance, TeamHistory, UserPresence } from '../types/attendance'

export async function getAttendanceToday(): Promise<AttendanceToday> {
    const res = await apiClient.get<AttendanceToday>('/attendance/me/today')
    return res.data
}

export async function checkIn(): Promise<AttendanceToday> {
    const res = await apiClient.post<AttendanceToday>('/attendance/check-in')
    return res.data
}

export async function checkOut(): Promise<AttendanceToday> {
    const res = await apiClient.post<AttendanceToday>('/attendance/check-out')
    return res.data
}

export async function startBreak(): Promise<AttendanceToday> {
    const res = await apiClient.post<AttendanceToday>('/attendance/break/start')
    return res.data
}

export async function endBreak(): Promise<AttendanceToday> {
    const res = await apiClient.post<AttendanceToday>('/attendance/break/end')
    return res.data
}

// Client-side idle detection reporting 5+ minutes of inactivity / activity resumed.
export async function reportIdle(): Promise<AttendanceToday> {
    const res = await apiClient.post<AttendanceToday>('/attendance/break/auto-start')
    return res.data
}

export async function reportActive(): Promise<AttendanceToday> {
    const res = await apiClient.post<AttendanceToday>('/attendance/break/auto-end')
    return res.data
}

export async function getAttendanceHistory(days = 30): Promise<AttendanceHistoryDay[]> {
    const res = await apiClient.get<AttendanceHistoryDay[]>('/attendance/me/history', { params: { days } })
    return res.data
}

export async function getTeamAttendance(): Promise<TeamAttendance> {
    const res = await apiClient.get<TeamAttendance>('/attendance/team')
    return res.data
}

export async function getTeamAttendanceHistory(days = 30): Promise<TeamHistory> {
    const res = await apiClient.get<TeamHistory>('/attendance/team/history', { params: { days } })
    return res.data
}

export async function getCompanyAttendance(): Promise<CompanyAttendance> {
    const res = await apiClient.get<CompanyAttendance>('/attendance/company')
    return res.data
}

export async function getUserPresence(): Promise<UserPresence[]> {
    const res = await apiClient.get<UserPresence[]>('/attendance/presence')
    return res.data
}
