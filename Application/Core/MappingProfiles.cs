using System;
using Application.AnnualLeaves.DTOs;
using Application.Departments.DTOs;
using Application.EmployeeProfiles.DTOs;
using Application.LeaveTypes.DTOs;
using Application.ProjectActivityTypes.DTOs;
using Application.Projects.DTOs;
using Application.Timesheets.DTOs;
using AutoMapper;
using Domain;

namespace Application.Core;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        CreateMap<AnnualLeave, AnnualLeave>();
        CreateMap<AnnualLeave, AnnualLeaveDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.EmployeeName, opt => opt.MapFrom(s => s.Employee != null ? s.Employee.DisplayName : string.Empty))
            .ForMember(d => d.DepartmentName, opt => opt.MapFrom(s => s.Department != null ? s.Department.Name : string.Empty));
        CreateMap<CreateAnnualLeaveRequest, AnnualLeave>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => AnnualLeaveStatus.Pending))
            .ForMember(d => d.CreatedAt, opt => opt.MapFrom(s => DateTime.UtcNow));
        CreateMap<EditAnnualLeaveRequest, AnnualLeave>();

        CreateMap<LeaveType, LeaveTypeDto>();
        CreateMap<UpsertLeaveTypeRequest, LeaveType>()
            .ForMember(d => d.Name, opt => opt.MapFrom(s => s.Name.Trim()))
            .ForMember(d => d.Icon, opt => opt.MapFrom(s => string.IsNullOrWhiteSpace(s.Icon) ? "\U0001F3F7️" : s.Icon.Trim()))
            .ForMember(d => d.ColorKey, opt => opt.MapFrom(s => string.IsNullOrWhiteSpace(s.ColorKey) ? "default" : s.ColorKey.Trim()))
            .ForMember(d => d.Description, opt => opt.MapFrom(s => (s.Description ?? string.Empty).Trim()))
            .ForMember(d => d.AllowanceUnit, opt => opt.MapFrom(s => string.IsNullOrWhiteSpace(s.AllowanceUnit) ? "days/year" : s.AllowanceUnit.Trim()))
            .ForMember(d => d.AccrualNotes, opt => opt.MapFrom(s => (s.AccrualNotes ?? string.Empty).Trim()))
            .ForMember(d => d.EligibilityNotes, opt => opt.MapFrom(s => string.IsNullOrWhiteSpace(s.EligibilityNotes) ? "All employees" : s.EligibilityNotes.Trim()))
            .ForMember(d => d.Id, opt => opt.Ignore())
            .ForMember(d => d.AnnualLeaves, opt => opt.Ignore());

        CreateMap<ProjectActivityType, ProjectActivityTypeDto>();
        CreateMap<UpsertProjectActivityTypeRequest, ProjectActivityType>()
            .ForMember(d => d.Name, opt => opt.MapFrom(s => s.Name.Trim()))
            .ForMember(d => d.Description, opt => opt.MapFrom(s => (s.Description ?? string.Empty).Trim()))
            .ForMember(d => d.Icon, opt => opt.MapFrom(s => string.IsNullOrWhiteSpace(s.Icon) ? "\U0001F3F7️" : s.Icon.Trim()))
            .ForMember(d => d.ColorKey, opt => opt.MapFrom(s => string.IsNullOrWhiteSpace(s.ColorKey) ? "default" : s.ColorKey.Trim()))
            .ForMember(d => d.Id, opt => opt.Ignore());

        CreateMap<Department, DepartmentDto>();
        CreateMap<DepartmentDto, Department>()
            .ForMember(d => d.UserDepartments, opt => opt.Ignore())
            .ForMember(d => d.EmployeeProfiles, opt => opt.Ignore())
            .ForMember(d => d.AnnualLeaves, opt => opt.Ignore())
            .ForMember(d => d.Projects, opt => opt.Ignore())
            .ForMember(d => d.Timesheets, opt => opt.Ignore());

        CreateMap<EmployeeProfile, EmployeeProfileDto>()
            .ForMember(d => d.DisplayName, opt => opt.MapFrom(s =>
                s.User != null
                    ? (s.User.DisplayName ?? s.User.UserName ?? s.UserId)
                    : s.UserId));

        CreateMap<Project, ProjectDto>()
            .ForMember(d => d.DepartmentName, opt => opt.MapFrom(s => s.Department != null ? s.Department.Name : null))
            .ForMember(d => d.OwnerName, opt => opt.MapFrom(s =>
                s.Owner != null
                    ? (s.Owner.DisplayName ?? s.Owner.UserName)
                    : null))
            .ForMember(d => d.HoursThisWeek, opt => opt.Ignore())
            .ForMember(d => d.HoursThisMonth, opt => opt.Ignore())
            .ForMember(d => d.HoursYTD, opt => opt.Ignore())
            .ForMember(d => d.TeamSize, opt => opt.Ignore())
            .ForMember(d => d.Team, opt => opt.Ignore());

        CreateMap<Timesheet, TimesheetDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.EmployeeName, opt => opt.MapFrom(s =>
                s.Employee != null && s.Employee.User != null
                    ? (s.Employee.User.DisplayName ?? s.Employee.User.UserName ?? s.EmployeeId)
                    : s.EmployeeId))
            .ForMember(d => d.ProjectSummaries, opt => opt.Ignore())
            .ForMember(d => d.DailyHours, opt => opt.Ignore());

        CreateMap<TimesheetEntry, TimesheetEntry>();
        CreateMap<TimesheetEntry, TimesheetProjectSummaryDto>()
            .ForMember(d => d.ProjectId, opt => opt.MapFrom(s => s.ProjectId))
            .ForMember(d => d.Code, opt => opt.MapFrom(s => s.Project != null ? s.Project.Code : string.Empty))
            .ForMember(d => d.Name, opt => opt.MapFrom(s => s.Project != null ? s.Project.Name : string.Empty))
            .ForMember(d => d.Hours, opt => opt.MapFrom(s => s.HoursWorked));
    }
}
