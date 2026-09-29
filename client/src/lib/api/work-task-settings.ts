import apiClient from './client'

export type FieldRequirement = 'Optional' | 'Required' | 'Hidden'

/** Mirrors `WorkTaskSettingsDto` — a full replace on save. */
export interface WorkTaskSettings {
    descriptionRequirement: FieldRequirement
    dueDateRequirement: FieldRequirement
    targetHoursRequirement: FieldRequirement
    attachmentsRequirement: FieldRequirement
    projectRequirement: FieldRequirement
    billableRequirement: FieldRequirement
    showPriority: boolean
    requireCompletionConfirmation: boolean
    maxAttachmentsPerTask: number
    maxAttachmentSizeMb: number
    allowImages: boolean
    allowPdf: boolean
    allowWord: boolean
    allowExcel: boolean
}

export async function getWorkTaskSettings() {
    const response = await apiClient.get<WorkTaskSettings>('/worktasksettings')
    return response.data
}

export async function updateWorkTaskSettings(settings: WorkTaskSettings) {
    const response = await apiClient.put<WorkTaskSettings>('/worktasksettings', settings)
    return response.data
}

/** Under `['work-tasks']`, so the `notificationsUpdated` handler in App.tsx refreshes it. */
export const WORK_TASK_SETTINGS_KEY = ['work-tasks', 'settings'] as const
