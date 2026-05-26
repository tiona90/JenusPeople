using System.ComponentModel.DataAnnotations;
using Domain;

namespace Application.AnnualLeaves.DTOs;

public class UpdateLeaveStatusRequest
{
    [Required]
    [EnumDataType(typeof(AnnualLeaveStatus))]
    public AnnualLeaveStatus Status { get; set; }

    [StringLength(500)]
    public string? StatusComment { get; set; }
}
