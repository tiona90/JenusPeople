import { describe, expect, it } from 'vitest'
import {
    ATTACHMENT_REQUIRED_MESSAGE, DEFAULT_TASK_SETTINGS, DUE_DATE_REQUIRED_MESSAGE, PROJECT_REQUIRED_MESSAGE,
    attachmentLimits, fieldRequirementError, isShown, needsAttachmentBeforeDone,
} from './task-settings'

const values = { description: '', dueDate: '', targetHours: null, projectId: 10, isBillable: true }

describe('task settings', () => {
    it('defaults reproduce today: project and billable required, the rest optional', () => {
        expect(fieldRequirementError(DEFAULT_TASK_SETTINGS, values)).toBeNull()
        expect(fieldRequirementError(DEFAULT_TASK_SETTINGS, { ...values, projectId: null })).toBe(PROJECT_REQUIRED_MESSAGE)
    })

    it('a required due date is refused blank, with the server message', () => {
        const s = { ...DEFAULT_TASK_SETTINGS, dueDateRequirement: 'Required' as const }
        expect(fieldRequirementError(s, values)).toBe(DUE_DATE_REQUIRED_MESSAGE)
    })

    it('a hidden field is never checked and never shown', () => {
        const s = { ...DEFAULT_TASK_SETTINGS, billableRequirement: 'Hidden' as const }
        expect(fieldRequirementError(s, { ...values, isBillable: null })).toBeNull()
        expect(isShown(s, 'billable')).toBe(false)
        expect(isShown({ ...DEFAULT_TASK_SETTINGS, showPriority: false }, 'priority')).toBe(false)
    })

    it('a required attachment holds Done until a file is on the task', () => {
        const s = { ...DEFAULT_TASK_SETTINGS, attachmentsRequirement: 'Required' as const }
        expect(needsAttachmentBeforeDone(s, 0)).toBe(true)
        expect(needsAttachmentBeforeDone(s, 1)).toBe(false)
        expect(needsAttachmentBeforeDone(DEFAULT_TASK_SETTINGS, 0)).toBe(false)
        expect(ATTACHMENT_REQUIRED_MESSAGE).toBe('Attach a file before marking this task done.')
    })

    it('attachment limits follow the settings', () => {
        const limits = attachmentLimits({ ...DEFAULT_TASK_SETTINGS, maxAttachmentsPerTask: 3, maxAttachmentSizeMb: 2, allowImages: false, allowWord: false })
        expect(limits.maxFiles).toBe(3)
        expect(limits.maxBytes).toBe(2 * 1024 * 1024)
        expect(limits.extensions).toEqual(['.pdf', '.xls', '.xlsx'])
    })
})
