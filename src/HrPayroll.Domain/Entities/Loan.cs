using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>سلفة موظف.</summary>
public class Loan : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public decimal Amount { get; set; }
    public DateTime LoanDate { get; set; }

    /// <summary>الشهر الذي تُخصم فيه السلفة.</summary>
    public int DeductionMonth { get; set; }
    public int DeductionYear { get; set; }

    public int Installments { get; set; } = 1;
    public string? Notes { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>كشف الرواتب الذي خُصمت فيه هذه السلفة (لمنع الخصم المزدوج).</summary>
    public int? PayrollItemId { get; set; }
    public PayrollItem? PayrollItem { get; set; }
}
