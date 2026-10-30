import apiClient from './client'
import type { AnnualLeave, CreateAnnualLeaveRequest, EditAnnualLeaveRequest } from '../types'
import { toPaged, type PageParams, type Paged } from './pagination'

// Returns the full array (server returns all rows when page/pageSize are absent).
// Kept zero-arg so it stays safe to pass directly as a React Query queryFn.
export async function getAnnualLeaves() {
    const response = await apiClient.get<AnnualLeave[]>('/annualleaves')
    return response.data
}

/**
 * Only requests still waiting on a decision — what the notification bell lists.
 * No parameters on purpose: it is passed bare as a queryFn.
 */
export async function getOpenAnnualLeaves() {
    const response = await apiClient.get<AnnualLeave[]>('/annualleaves', { params: { openOnly: true } })
    return response.data
}

// Paged variant: returns items + total (read from the X-Total-Count header).
export async function getAnnualLeavesPaged(params: PageParams): Promise<Paged<AnnualLeave>> {
    const response = await apiClient.get<AnnualLeave[]>('/annualleaves', { params })
    return toPaged(response, params)
}

export async function getTeamAwayThisWeekCount() {
    const response = await apiClient.get<number>('/annualleaves/team-away-this-week/count')
    return response.data
}

export async function getAnnualLeaveDetails(id: string) {
    const response = await apiClient.get<AnnualLeave>(`/annualleaves/${id}`)
    return response.data
}

export async function uploadLeaveEvidence(file: File) {
    const formData = new FormData()
    formData.append('file', file)

    const response = await apiClient.post<{ evidenceUrl: string; fileName: string }>(
        '/annualleaves/evidence-upload',
        formData,
        {
            headers: {
                'Content-Type': 'multipart/form-data',
            },
        }
    )

    return response.data
}

/**
 * A handover document for the colleague covering the leave. Its own endpoint and
 * purpose rather than `uploadLeaveEvidence`: the delegate may read this back and
 * must not thereby be able to read a medical certificate.
 */
export async function uploadCoverageHandover(file: File) {
    const formData = new FormData()
    formData.append('file', file)

    const response = await apiClient.post<{ coverageAttachmentUrl: string; fileName: string }>(
        '/annualleaves/coverage-upload',
        formData,
        {
            headers: {
                'Content-Type': 'multipart/form-data',
            },
        }
    )

    return response.data
}

export async function createAnnualLeave(request: CreateAnnualLeaveRequest) {
    const response = await apiClient.post<string>('/annualleaves', request)
    return response.data
}

export async function editAnnualLeave(request: EditAnnualLeaveRequest) {
    await apiClient.put('/annualleaves', request)
}

export async function deleteAnnualLeave(id: string) {
    await apiClient.delete(`/annualleaves/${id}`)
}

export async function updateLeaveStatus(
    id: string,
    status: 'Approved' | 'Rejected' | 'Cancelled',
    statusComment?: string
) {
    await apiClient.patch(`/annualleaves/${id}/status`, { status, statusComment })
}
