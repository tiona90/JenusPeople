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
  departmentId: number;
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
}
