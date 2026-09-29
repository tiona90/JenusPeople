import { useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import { addWorkTaskAttachment, removeWorkTaskAttachment } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { resolveFileUrl } from '../../lib/api/file-url'
import {
    DEFAULT_ATTACHMENT_LIMITS, acceptFor, formatFileSize, taskAttachmentError,
} from '../../lib/task-attachments'
import type { TaskAttachmentLimits } from '../../lib/task-attachments'
import type { WorkTask, WorkTaskAttachment } from '../../lib/types'

/* ─── pieces ─────────────────────────────────────────────────────────────── */

function FileRow({ name, href, detail, download, onRemove, removeLabel, disabled }: {
    name: string
    href?: string
    detail: string
    /** Offer a Download link beside the name, which opens the file in a new tab. */
    download?: boolean
    onRemove?: () => void
    removeLabel: string
    disabled?: boolean
}) {
    return (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', py: '4px', minWidth: 0 }}>
            <Box component="span" aria-hidden sx={{ fontSize: 13 }}>📎</Box>
            <Box sx={{ minWidth: 0, flex: 1 }}>
                {href ? (
                    <Box
                        component="a"
                        href={href}
                        target="_blank"
                        rel="noopener noreferrer"
                        sx={{
                            display: 'block', fontSize: 12, fontWeight: 600, color: 'primary.main', textDecoration: 'none',
                            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            '&:hover': { textDecoration: 'underline' },
                        }}
                    >
                        {name}
                    </Box>
                ) : (
                    <Box sx={{ fontSize: 12, fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{name}</Box>
                )}
                <Box sx={{ fontSize: 10, color: 'text.secondary' }}>{detail}</Box>
            </Box>
            {href && download && (
                <Box
                    component="a"
                    href={href}
                    download={name}
                    aria-label={`Download ${name}`}
                    sx={{
                        fontSize: 11, fontWeight: 600, color: 'primary.main', textDecoration: 'none', whiteSpace: 'nowrap',
                        border: '1px solid', borderColor: 'divider', borderRadius: '6px', px: '8px', py: '3px',
                        '&:hover': { borderColor: 'primary.main' },
                    }}
                >
                    ⤓ Download
                </Box>
            )}
            {onRemove && (
                <Box
                    component="button"
                    type="button"
                    aria-label={removeLabel}
                    title={removeLabel}
                    disabled={disabled}
                    onClick={onRemove}
                    sx={{
                        bgcolor: 'transparent', border: 'none', color: 'text.secondary', cursor: 'pointer',
                        fontSize: 16, lineHeight: 1, px: '4px', fontFamily: 'inherit',
                        '&:hover:not(:disabled)': { color: 'error.main' },
                        '&:disabled': { opacity: 0.5, cursor: 'default' },
                    }}
                >
                    ×
                </Box>
            )}
        </Box>
    )
}

function AttachButton({ onFiles, disabled, label, accept }: { onFiles: (files: File[]) => void; disabled?: boolean; label: string; accept: string }) {
    const input = useRef<HTMLInputElement>(null)
    return (
        <>
            <Box
                component="button"
                type="button"
                disabled={disabled}
                onClick={() => input.current?.click()}
                sx={{
                    bgcolor: 'transparent', border: 'none', p: 0, color: 'primary.main', cursor: 'pointer',
                    fontSize: 12, fontWeight: 600, fontFamily: 'inherit',
                    '&:hover:not(:disabled)': { textDecoration: 'underline' },
                    '&:disabled': { color: 'text.disabled', cursor: 'default' },
                }}
            >
                {label}
            </Box>
            <input
                ref={input}
                type="file"
                multiple
                hidden
                accept={accept}
                data-testid="task-attachment-input"
                onChange={(e) => {
                    const files = Array.from(e.target.files ?? [])
                    // Cleared so picking the same file again still fires a change.
                    e.target.value = ''
                    if (files.length > 0) onFiles(files)
                }}
            />
        </>
    )
}

/** Refuses up front what the API is certain to: a wrong kind, too large, or past the limit. */
function checkFiles(files: File[], alreadyAttached: number, limits: TaskAttachmentLimits): { accepted: File[]; errors: string[] } {
    const errors: string[] = []
    const accepted: File[] = []
    for (const file of files) {
        const error = taskAttachmentError(file, limits)
        if (error) errors.push(error)
        else if (alreadyAttached + accepted.length >= limits.maxFiles)
            errors.push(`${file.name}: a task can carry at most ${limits.maxFiles} attachments.`)
        else accepted.push(file)
    }
    return { accepted, errors }
}

function ErrorLine({ children }: { children: React.ReactNode }) {
    return <Box role="alert" sx={{ fontSize: 11, color: 'error.main', mt: '4px', whiteSpace: 'pre-line' }}>{children}</Box>
}

/* ─── live: a saved task ─────────────────────────────────────────────────── */

/**
 * The files on a saved task, uploaded and removed as they are picked. Only whoever
 * may edit the task attaches (`canAttach`, the task's `canEdit`); everyone who sees
 * it opens them, and each file's own `canRemove` says who may take it off.
 */
export default function TaskAttachments({ taskId, attachments, canAttach, downloadable = false, onChanged, limits = DEFAULT_ATTACHMENT_LIMITS }: {
    taskId: number
    attachments: WorkTaskAttachment[]
    canAttach: boolean
    /** A Download link on every file (the details dialog). */
    downloadable?: boolean
    /** The task as the server returns it after each change. */
    onChanged?: (task: WorkTask) => void
    /** The Task Settings' current limits; defaults to today's behaviour. */
    limits?: TaskAttachmentLimits
}) {
    const queryClient = useQueryClient()
    const [errors, setErrors] = useState<string[]>([])

    const upload = useMutation({
        // One at a time, so a refused file stops nothing already sent, and each is reported by name.
        mutationFn: async (files: File[]) => {
            let latest: WorkTask | null = null
            const failed: string[] = []
            for (const file of files) {
                try {
                    latest = await addWorkTaskAttachment(taskId, file)
                } catch (error) {
                    failed.push(`${file.name}: ${getApiErrorMessage(error, 'could not be attached.')}`)
                }
            }
            return { latest, failed }
        },
        onSuccess: ({ latest, failed }) => {
            setErrors((current) => [...current, ...failed])
            if (latest) onChanged?.(latest)
            void queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
        },
    })
    const remove = useMutation({
        mutationFn: (attachmentId: number) => removeWorkTaskAttachment(taskId, attachmentId),
        onSuccess: (task) => {
            onChanged?.(task)
            void queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
        },
        onError: (error) => setErrors([getApiErrorMessage(error, 'The file could not be removed.')]),
    })

    const attach = (files: File[]) => {
        const { accepted, errors: refused } = checkFiles(files, attachments.length, limits)
        setErrors(refused)
        if (accepted.length > 0) upload.mutate(accepted)
    }

    const busy = upload.isPending || remove.isPending
    const full = attachments.length >= limits.maxFiles
    return (
        <Box>
            {attachments.map((a) => (
                <FileRow
                    key={a.id}
                    name={a.fileName}
                    href={resolveFileUrl(a.url)}
                    detail={`${formatFileSize(a.sizeBytes)} · ${a.uploadedByName}`}
                    download={downloadable}
                    onRemove={a.canRemove ? () => remove.mutate(a.id) : undefined}
                    removeLabel={`Remove ${a.fileName}`}
                    disabled={busy}
                />
            ))}
            {canAttach && <Box sx={{ mt: attachments.length > 0 ? '4px' : 0 }}>
                <AttachButton
                    onFiles={attach}
                    disabled={busy || full}
                    accept={acceptFor(limits)}
                    label={upload.isPending ? 'Uploading…' : full ? `${limits.maxFiles} files attached (the most a task can carry)` : '+ Attach files'}
                />
            </Box>}
            {errors.length > 0 && <ErrorLine>{errors.join('\n')}</ErrorLine>}
        </Box>
    )
}

/* ─── staged: a task not yet created ─────────────────────────────────────── */

/** Files picked for a task that does not exist yet; the dialog uploads them once it does. */
export function StagedTaskAttachments({ files, onChange, limits = DEFAULT_ATTACHMENT_LIMITS }: {
    files: File[]
    onChange: (files: File[]) => void
    /** The Task Settings' current limits; defaults to today's behaviour. */
    limits?: TaskAttachmentLimits
}) {
    const [errors, setErrors] = useState<string[]>([])
    const full = files.length >= limits.maxFiles
    return (
        <Box>
            {files.map((file, i) => (
                <FileRow
                    key={`${file.name}-${i}`}
                    name={file.name}
                    detail={`${formatFileSize(file.size)} · uploaded when the task is created`}
                    onRemove={() => onChange(files.filter((_, j) => j !== i))}
                    removeLabel={`Remove ${file.name}`}
                />
            ))}
            <Box sx={{ mt: files.length > 0 ? '4px' : 0 }}>
                <AttachButton
                    onFiles={(picked) => {
                        const { accepted, errors: refused } = checkFiles(picked, files.length, limits)
                        setErrors(refused)
                        if (accepted.length > 0) onChange([...files, ...accepted])
                    }}
                    disabled={full}
                    accept={acceptFor(limits)}
                    label={full ? `${limits.maxFiles} files attached (the most a task can carry)` : '+ Attach files'}
                />
            </Box>
            {errors.length > 0 && <ErrorLine>{errors.join('\n')}</ErrorLine>}
        </Box>
    )
}
