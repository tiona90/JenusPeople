import type { Project } from './project';
import type { ProjectActivityType } from './project-activity-type';

export interface TimesheetEntry {
    id: string;
    timesheetId: string;
    projectId: number;
    project?: Project;
    date: string;
    hoursWorked: number;
    notes?: string | null;
    activityTypeId?: number | null;
    activityType?: ProjectActivityType;
}
