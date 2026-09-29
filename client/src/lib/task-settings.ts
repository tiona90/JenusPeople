/**
 * The System Administrator's Task Settings, as every task surface reads them.
 * Mirrors `WorkTaskFieldRules` on the server — keep the messages identical — so the
 * dialog holds Save and the card holds Done where the API is certain to refuse.
 */
import type { FieldRequirement, WorkTaskSettings } from './api/work-task-settings'
import type { TaskAttachmentLimits } from './task-attachments'

export const DESCRIPTION_REQUIRED_MESSAGE = 'Description is required.'
export const DUE_DATE_REQUIRED_MESSAGE = 'Due date is required.'
export const TARGET_HOURS_REQUIRED_MESSAGE = 'Target hours are required.'
export const PROJECT_REQUIRED_MESSAGE = 'Project is required.'
export const BILLABLE_REQUIRED_MESSAGE = 'Say whether the task is billable.'
export const ATTACHMENT_REQUIRED_MESSAGE = 'Attach a file before marking this task done.'

/** Today's behaviour; also what an older API with no settings endpoint reads as. */
export const DEFAULT_TASK_SETTINGS: WorkTaskSettings = {
    descriptionRequirement: 'Optional',
    dueDateRequirement: 'Optional',
    targetHoursRequirement: 'Optional',
    attachmentsRequirement: 'Optional',
    projectRequirement: 'Required',
    billableRequirement: 'Required',
    showPriority: true,
    requireCompletionConfirmation: true,
    maxAttachmentsPerTask: 10,
    maxAttachmentSizeMb: 10,
    allowImages: true,
    allowPdf: true,
    allowWord: true,
    allowExcel: true,
}

export type TaskField = 'description' | 'dueDate' | 'targetHours' | 'attachments' | 'project' | 'billable'

/** Which values each field may take — mirrors UpdateWorkTaskSettingsValidator. */
export const FIELD_OPTIONS: Record<TaskField, FieldRequirement[]> = {
    description: ['Required', 'Optional', 'Hidden'],
    dueDate: ['Required', 'Optional', 'Hidden'],
    targetHours: ['Required', 'Optional', 'Hidden'],
    attachments: ['Required', 'Optional', 'Hidden'],
    project: ['Required', 'Optional'],
    billable: ['Required', 'Hidden'],
}

const KEYS: Record<TaskField, keyof WorkTaskSettings> = {
    description: 'descriptionRequirement',
    dueDate: 'dueDateRequirement',
    targetHours: 'targetHoursRequirement',
    attachments: 'attachmentsRequirement',
    project: 'projectRequirement',
    billable: 'billableRequirement',
}

export function requirementOf(s: WorkTaskSettings, field: TaskField): FieldRequirement {
    return s[KEYS[field]] as FieldRequirement
}

export function withRequirement(s: WorkTaskSettings, field: TaskField, value: FieldRequirement): WorkTaskSettings {
    return { ...s, [KEYS[field]]: value }
}

export function isShown(s: WorkTaskSettings, field: TaskField | 'priority'): boolean {
    return field === 'priority' ? s.showPriority : requirementOf(s, field) !== 'Hidden'
}

export interface TaskFieldValues {
    description: string
    dueDate: string
    targetHours: number | null
    projectId: number | null
    isBillable: boolean | null
}

/** The first required field left blank, in the server's order, or null. */
export function fieldRequirementError(s: WorkTaskSettings, v: TaskFieldValues): string | null {
    if (s.projectRequirement === 'Required' && v.projectId == null) return PROJECT_REQUIRED_MESSAGE
    if (s.billableRequirement === 'Required' && v.isBillable == null) return BILLABLE_REQUIRED_MESSAGE
    if (s.descriptionRequirement === 'Required' && v.description.trim() === '') return DESCRIPTION_REQUIRED_MESSAGE
    if (s.dueDateRequirement === 'Required' && v.dueDate === '') return DUE_DATE_REQUIRED_MESSAGE
    if (s.targetHoursRequirement === 'Required' && v.targetHours == null) return TARGET_HOURS_REQUIRED_MESSAGE
    return null
}

export function needsAttachmentBeforeDone(s: WorkTaskSettings, attachmentCount: number): boolean {
    return s.attachmentsRequirement === 'Required' && attachmentCount === 0
}

export function attachmentLimits(s: WorkTaskSettings): TaskAttachmentLimits {
    const extensions = [
        ...(s.allowPdf ? ['.pdf'] : []),
        ...(s.allowWord ? ['.doc', '.docx'] : []),
        ...(s.allowExcel ? ['.xls', '.xlsx'] : []),
        ...(s.allowImages ? ['.jpg', '.jpeg', '.png'] : []),
    ]
    return { maxFiles: s.maxAttachmentsPerTask, maxBytes: s.maxAttachmentSizeMb * 1024 * 1024, extensions }
}
