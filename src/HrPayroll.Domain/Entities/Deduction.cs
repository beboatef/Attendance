using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>خصم أو جزاء.</summary>
public class Deduction : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public decimal Amount { get; set; }
    public DateTime DeductionDate { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int Month { get; set; }
    public int Year { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>كشف الرواتب الذي خُصم فيه (لمنع الخصم المزدوج).</summary>
    public int? PayrollItemId { get; set; }
    public PayrollItem? PayrollItem { get; set; }
}
