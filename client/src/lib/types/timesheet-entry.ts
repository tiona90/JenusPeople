import type { Project } from './project';
import type { ProjectActivityType } from './project-activity-type';
import type { ProjectType } from './project-type';

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
    // Which kind of engagement this row's work was — one of the types its
    // project is classified as. Null on every entry predating the field, and on
    // rows logged against an unclassified project.
    projectTypeId?: number | null;
    projectType?: ProjectType;
}
