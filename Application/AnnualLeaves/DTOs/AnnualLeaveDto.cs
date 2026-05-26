using System;
using System.ComponentModel.DataAnnotations;

namespace Application.AnnualLeaves.DTOs;

public class AnnualLeaveDto
{

    public string Id { get; set; } = string.Empty;


    public string EmployeeId { get; set; } = string.Empty;

    public int? LeaveTypeId { get; set; }


    public DateTime StartDate { get; set; }


    public DateTime EndDate { get; set; }


    public string Reason { get; set; } = string.Empty;

    public string? EvidenceUrl { get; set; }

    public string Status { get; set; } = string.Empty;


    public DateTime CreatedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }


    [Range(1, int.MaxValue)]
    public int TotalDays { get; set; }

    public string EmployeeName { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;
}