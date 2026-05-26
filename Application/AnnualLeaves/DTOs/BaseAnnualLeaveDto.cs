using System;
using System.ComponentModel.DataAnnotations;

namespace Application.AnnualLeaves.DTOs;

public class BaseAnnualLeaveDto
{
    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [Range(1, int.MaxValue)]
    public int LeaveTypeId { get; set; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason is required and must be at most 500 characters.")]
    public string Reason { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? EvidenceUrl { get; set; }
}