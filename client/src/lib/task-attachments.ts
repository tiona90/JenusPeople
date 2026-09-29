/**
 * Files attached to a task. Mirrors the server's `StoredFilePurpose.TaskAttachment`
 * policy in `StoreFile` (images, PDF, Word, Excel; 10 MB) and
 * `WorkTaskAttachment.MaxPerTask`, so the page refuses up front what the API is
 * certain to. The server still checks the bytes; this only reads the name and size.
 *
 * The actual limits are the Task Settings' — see `lib/task-settings.ts`'s
 * `attachmentLimits`, which turns the settings into a `TaskAttachmentLimits`.
 * `DEFAULT_ATTACHMENT_LIMITS` below is only what an older API with no settings
 * endpoint, or one not yet loaded, reads as.
 */

export interface TaskAttachmentLimits {
    maxFiles: number
    maxBytes: number
    extensions: string[]
}

const KIND_NAMES: Record<string, string> = {
    '.pdf': 'PDF', '.doc': 'Word', '.docx': 'Word', '.xls': 'Excel', '.xlsx': 'Excel', '.jpg': 'JPG', '.jpeg': 'JPG', '.png': 'PNG',
}

/** Today's limits, and the Task Settings defaults: 10 files, 10 MB, PDF, Word, Excel and images. */
export const DEFAULT_ATTACHMENT_LIMITS: TaskAttachmentLimits = {
    maxFiles: 10,
    maxBytes: 10 * 1024 * 1024,
    extensions: ['.pdf', '.doc', '.docx', '.xls', '.xlsx', '.jpg', '.jpeg', '.png'],
}

/** For the file input's `accept`. */
export function acceptFor(limits: TaskAttachmentLimits): string {
    return limits.extensions.join(',')
}

/** "PDF, Word and JPG" — the kinds a limit allows, for messages and helper text. */
export function describeKinds(limits: TaskAttachmentLimits): string {
    const names = [...new Set(limits.extensions.map((e) => KIND_NAMES[e]))]
    return names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

/** Why this file would be refused, or null when it looks acceptable. */
export function taskAttachmentError(file: Pick<File, 'name' | 'size'>, limits: TaskAttachmentLimits = DEFAULT_ATTACHMENT_LIMITS): string | null {
    const dot = file.name.lastIndexOf('.')
    const extension = dot === -1 ? '' : file.name.slice(dot).toLowerCase()
    if (!limits.extensions.includes(extension)) return `${file.name}: only ${describeKinds(limits)} files can be attached.`
    if (file.size > limits.maxBytes) return `${file.name} is larger than the ${Math.round(limits.maxBytes / 1024 / 1024)}MB limit.`
    return null
}

export function formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
