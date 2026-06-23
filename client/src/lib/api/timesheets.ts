import apiClient from './client';
import type { Timesheet } from '../types/timesheet';
import { toPaged, type PageParams, type Paged } from './pagination';

// Zero-arg so it stays safe to pass directly as a React Query queryFn.
export async function getTimesheets(): Promise<Timesheet[]> {
    const res = await apiClient.get('/timesheets');
    return res.data;
}

// Paged variant: returns items + total (read from the X-Total-Count header).
export async function getTimesheetsPaged(params: PageParams): Promise<Paged<Timesheet>> {
    const res = await apiClient.get<Timesheet[]>('/timesheets', { params });
    return toPaged(res, params);
}

export async function getMyTimesheets(): Promise<Timesheet[]> {
    const res = await apiClient.get('/timesheets', { params: { myOnly: true } });
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
