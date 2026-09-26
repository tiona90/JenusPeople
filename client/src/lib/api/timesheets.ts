import apiClient from './client';
import type { Timesheet } from '../types/timesheet';
import { toPaged, type PageParams, type Paged } from './pagination';

// Zero-arg so it stays safe to pass directly as a React Query queryFn.
// The day-by-day comparison with attendance (attendanceMinutes, dayMismatchMinutes,
// onLeaveDays, mismatchDayCount) is opt-in: the server only loads attendance when
// asked. Pass it from a page that renders the flags, never from the bell, which
// polls this list every 15 seconds. `=== true` rather than truthiness, because a
// bare `queryFn: getTimesheets` hands this slot React Query's context object.
export interface TimesheetListOptions {
    includeAttendance?: boolean
}

function attendanceParams(options?: TimesheetListOptions) {
    return options?.includeAttendance === true ? { includeAttendance: true } : {}
}

export async function getTimesheets(options?: TimesheetListOptions): Promise<Timesheet[]> {
    const res = await apiClient.get('/timesheets', { params: attendanceParams(options) });
    return res.data;
}

// Paged variant: returns items + total (read from the X-Total-Count header).
export async function getTimesheetsPaged(params: PageParams): Promise<Paged<Timesheet>> {
    const res = await apiClient.get<Timesheet[]>('/timesheets', { params });
    return toPaged(res, params);
}

export async function getMyTimesheets(options?: TimesheetListOptions): Promise<Timesheet[]> {
    const res = await apiClient.get('/timesheets', { params: { myOnly: true, ...attendanceParams(options) } });
    return res.data;
}

export async function getTimesheet(id: string): Promise<Timesheet> {
    const res = await apiClient.get(`/timesheets/${id}`);
    return res.data;
}

export async function createTimesheet(data: { periodStart: string; periodEnd: string }): Promise<Timesheet> {
    const res = await apiClient.post('/timesheets', data);
    return res.data;
}

export async function updateTimesheet(id: string, data: Partial<Timesheet>): Promise<Timesheet> {
    const res = await apiClient.put(`/timesheets/${id}`, data);
    return res.data;
}

export async function deleteTimesheet(id: string): Promise<void> {
    await apiClient.delete(`/timesheets/${id}`);
}

export async function submitTimesheet(id: string): Promise<void> {
    await apiClient.patch(`/timesheets/${id}/submit`);
}

export async function approveTimesheet(id: string): Promise<void> {
    await apiClient.patch(`/timesheets/${id}/approve`);
}

export async function rejectTimesheet(id: string, comment: string): Promise<void> {
    await apiClient.patch(`/timesheets/${id}/reject`, { comment });
}

/** The HR Administrator takes an approval back: the sheet returns to Submitted for the manager to review again. */
export async function reopenTimesheet(id: string, comment: string): Promise<void> {
    await apiClient.patch(`/timesheets/${id}/reopen`, { comment });
}
