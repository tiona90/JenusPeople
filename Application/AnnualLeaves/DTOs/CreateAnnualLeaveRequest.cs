using System;
using System.ComponentModel.DataAnnotations;

namespace Application.AnnualLeaves.DTOs;

public class CreateAnnualLeaveRequest : BaseAnnualLeaveDto
{
    [Required]
    public string EmployeeId { get; set; } = string.Empty;
}