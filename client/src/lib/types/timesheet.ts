import type { TimesheetEntry } from './timesheet-entry';
import type { TimesheetStatusHistory } from './timesheet-status-history';

export type TimesheetStatus =
  | 'Draft'
  | 'Submitted'
  | 'Approved'
  | 'Rejected'
  | 'Resubmitted';

export interface TimesheetProjectSummary {
  projectId: number;
  code: string;
  name: string;
  hours: number;
}

export interface Timesheet {
  id: string;
  employeeId: string;
  employeeName: string; // Added for display
  /** Null when the author has no department — a System Administrator. */
  departmentId: number | null;
  periodStart: string;
  periodEnd: string;
  totalHours: number;
  status: TimesheetStatus;
  approverId?: string | null;
  submittedAt?: string | null;
  approvedAt?: string | null;
  createdAt: string;
  entries?: TimesheetEntry[];
  statusHistory?: TimesheetStatusHistory[];
  projectSummaries?: TimesheetProjectSummary[];
  /** Hours per weekday — index 0 = Monday … 4 = Friday. */
  dailyHours?: number[];
  /**
   * A submitted timesheet a manager is available to review today, so it is the
   * manager's, not HR's (server `TimesheetReviewRule`). Absent on an API predating
   * the flag, which reads as not with the manager.
   */
  awaitingManager?: boolean;
  /**
   * Minutes attendance recorded per weekday, parallel to dailyHours. Absent or
   * null for an older API and for a sheet the server does not compare (older than
   * its comparison window).
   */
  attendanceMinutes?: number[] | null;
  /** Logged minus attended per weekday beyond the grace; null where the day agrees. */
  dayMismatchMinutes?: (number | null)[] | null;
  /** Approved full-day leave per weekday. */
  onLeaveDays?: boolean[] | null;
  /** How many weekdays disagree with attendance. */
  mismatchDayCount?: number;
}
