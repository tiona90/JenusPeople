import apiClient from './client';
import type { Timesheet } from '../types/timesheet';

export async function getTimesheets(): Promise<Timesheet[]> {
    const res = await apiClient.get('/timesheets');
    return res.data;
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
