import type { Project } from './project';
import type { ProjectActivityType } from './project-activity-type';
import type { ProjectComponent } from './project-component';
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
    // Which part of the product it was done on — one of the components its
    // project is made up of. Null for the same reasons.
    projectComponentId?: number | null;
    projectComponent?: ProjectComponent;
    // Which of the owner's tasks the hours went on; counted towards its target.
    // Null on every entry predating the link, and on rows logged against no task.
    workTaskId?: number | null;
}
