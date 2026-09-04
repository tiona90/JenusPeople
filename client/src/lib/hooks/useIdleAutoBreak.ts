import { useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { reportActive, reportIdle } from '../api/attendance'
import type { AttendanceToday } from '../types/attendance'
import { attendanceQueryKey } from './useAttendance'

const IDLE_THRESHOLD_MS = 5 * 60_000
const POLL_INTERVAL_MS = 15_000
const ACTIVITY_EVENTS = ['mousemove', 'mousedown', 'keydown', 'wheel', 'touchstart', 'scroll'] as const

/**
 * Client-side idle detection: after five minutes with no mouse/keyboard/scroll
 * activity, reports an automatic break; reverts the instant activity resumes.
 *
 * Deliberately does not touch a break the user started by clicking "Break"
 * themselves — see `today.isAutoBreak` and the `IsAutomatic` guard on
 * `EndBreak` — so a real break is never silently ended by a stray mouse
 * movement.
 */
export function useIdleAutoBreak(today: AttendanceToday | undefined) {
    const qc = useQueryClient()
    const lastActivityRef = useRef(0)
    const todayRef = useRef(today)
    const resumeInFlightRef = useRef(false)

    useEffect(() => {
        todayRef.current = today
    }, [today])

    useEffect(() => {
        lastActivityRef.current = Date.now()

        const markActive = () => {
            lastActivityRef.current = Date.now()

            const current = todayRef.current
            if (current?.status === 'break' && current.isAutoBreak && !resumeInFlightRef.current) {
                resumeInFlightRef.current = true
                reportActive()
                    .then((data) => qc.setQueryData(attendanceQueryKey, data))
                    .catch(() => {
                        // Best-effort background reconciliation; a failed call just
                        // gets retried on the next activity event or poll tick.
                    })
                    .finally(() => {
                        resumeInFlightRef.current = false
                    })
            }
        }

        for (const evt of ACTIVITY_EVENTS) {
            window.addEventListener(evt, markActive, { passive: true })
        }
        return () => {
            for (const evt of ACTIVITY_EVENTS) {
                window.removeEventListener(evt, markActive)
            }
        }
    }, [qc])

    useEffect(() => {
        const id = window.setInterval(() => {
            const current = todayRef.current
            if (current?.status !== 'in') return
            if (Date.now() - lastActivityRef.current < IDLE_THRESHOLD_MS) return

            reportIdle()
                .then((data) => qc.setQueryData(attendanceQueryKey, data))
                .catch(() => {})
        }, POLL_INTERVAL_MS)

        return () => window.clearInterval(id)
    }, [qc])
}
