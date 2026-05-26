using System;
using System.ComponentModel.DataAnnotations;
using Domain;

namespace Application.AnnualLeaves.DTOs;

public class EditAnnualLeaveRequest : BaseAnnualLeaveDto
{
    [Required]
    public string Id { get; set; } = string.Empty;

    public AnnualLeaveStatus? Status { get; set; }

    [StringLength(500)]
    public string? StatusComment { get; set; }
}