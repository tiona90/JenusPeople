/* eslint-disable react-refresh/only-export-components -- intentional: dialog component and matching button sx tokens ship together */
import type { ReactNode } from 'react'
import type { Breakpoint } from '@mui/material'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'

const C_BORDER = '#E4E6EA'

export function AppDialog({
    open,
    onClose,
    maxWidth = 'sm',
    children,
}: {
    open: boolean
    onClose: () => void
    maxWidth?: Breakpoint | false
    children: ReactNode
}) {
    return (
        <Dialog
            open={open}
            onClose={onClose}
            maxWidth={maxWidth}
            fullWidth
            PaperProps={{
                sx: {
                    borderRadius: '12px',
                    boxShadow: '0 20px 60px rgba(0,0,0,0.12)',
                },
            }}
        >
            {children}
        </Dialog>
    )
}

export function AppDialogTitle({ children }: { children: ReactNode }) {
    return (
        <DialogTitle
            sx={{
                px: 3,
                py: 2,
                fontSize: 15,
                fontWeight: 600,
                color: '#1A1A2E',
                borderBottom: `1px solid ${C_BORDER}`,
            }}
        >
            {children}
        </DialogTitle>
    )
}

export function AppDialogContent({ children }: { children: ReactNode }) {
    return (
        <DialogContent sx={{ px: 3, pt: '20px !important', pb: 2 }}>
            {children}
        </DialogContent>
    )
}

export function AppDialogActions({ children }: { children: ReactNode }) {
    return (
        <DialogActions
            sx={{
                px: 3,
                py: 2,
                borderTop: `1px solid ${C_BORDER}`,
                gap: 1,
            }}
        >
            {children}
        </DialogActions>
    )
}

/** Consistent sx for the outlined Cancel button */
export const cancelBtnSx = {
    textTransform: 'none',
    color: '#6B7280',
    borderColor: '#D1D5DB',
    '&:hover': { bgcolor: '#F4F5F7', borderColor: '#D1D5DB' },
} as const

/** Consistent sx for the primary Save/Confirm button */
export const saveBtnSx = {
    textTransform: 'none',
    bgcolor: '#4F8EF7',
    '&:hover': { bgcolor: '#3A7AE4' },
    boxShadow: 'none',
} as const

/** Consistent sx for a destructive Confirm/Delete button */
export const dangerBtnSx = {
    textTransform: 'none',
    bgcolor: '#FF4D4F',
    '&:hover': { bgcolor: '#E03C3E' },
    boxShadow: 'none',
} as const
