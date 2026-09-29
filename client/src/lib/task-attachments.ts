/**
 * Files attached to a task. Mirrors the server's `StoredFilePurpose.TaskAttachment`
 * policy in `StoreFile` (images, PDF, Word, Excel; 10 MB) and
 * `WorkTaskAttachment.MaxPerTask`, so the page refuses up front what the API is
 * certain to. The server still checks the bytes; this only reads the name and size.
 */

/** Mirrors `WorkTaskAttachment.MaxPerTask`. */
export const MAX_TASK_ATTACHMENTS = 10

/** Mirrors the 10 MB ceiling on `StoredFilePurpose.TaskAttachment`. */
export const MAX_TASK_ATTACHMENT_BYTES = 10 * 1024 * 1024

const EXTENSIONS = ['.pdf', '.doc', '.docx', '.xls', '.xlsx', '.jpg', '.jpeg', '.png']

/** For the file input's `accept`. */
export const TASK_ATTACHMENT_ACCEPT = EXTENSIONS.join(',')

/** Why this file would be refused, or null when it looks acceptable. */
export function taskAttachmentError(file: Pick<File, 'name' | 'size'>): string | null {
    const dot = file.name.lastIndexOf('.')
    const extension = dot === -1 ? '' : file.name.slice(dot).toLowerCase()
    if (!EXTENSIONS.includes(extension)) return `${file.name}: only PDF, Word, Excel, JPG and PNG files can be attached.`
    if (file.size > MAX_TASK_ATTACHMENT_BYTES) return `${file.name} is larger than the 10MB limit.`
    return null
}

export function formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
