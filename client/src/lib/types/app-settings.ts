export type ReminderFrequency = 'daily' | 'weekly'

export interface ReminderSetting {
    id: string
    enabled: boolean
    time: string // "HH:mm"
    frequency: ReminderFrequency
}

export interface AppSettings {
    leaveYearStartMonth: number
    maxCarryoverDays: number
    defaultAnnualEntitlement: number
    yearEndWarningDays: number
    finalWarningDays: number
    autoRunRollover: boolean
    sendYearEndWarningEmails: boolean
    blockLeaveSpanningIntoNextYear: boolean
    notifyManagersOfTeamExpiries: boolean
    holidayCountryCode: string | null
    holidayCountryName: string | null

    // Organization
    workingHoursStart: string // "HH:mm"
    workingHoursEnd: string // "HH:mm"
    timeZoneId: string
    financialYearStartMonth: number
    workingDays: string // "mon-fri" | "mon-sat" | "sun-fri" | "custom"
    workingDaysCustom: string // CSV of day tokens, e.g. "mon,wed,fri" (used when workingDays === "custom")

    // Email notifications
    emailNotificationsEnabled: boolean
    emailDailyDigest: boolean
    emailUrgentOnly: boolean

    // Slack
    slackEnabled: boolean
    slackConnected: boolean // read-only: webhook configured server-side

    // Reminders
    reminders: ReminderSetting[]
}
