using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>اعتماد الإضافي لفترة موظف (يحفظ المحسوب والمعتمد).</summary>
public class OvertimeApproval : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>الساعات المحسوبة آليًا.</summary>
    public decimal CalculatedHours { get; set; }

    /// <summary>الساعات المعتمدة يدويًا.</summary>
    public decimal ApprovedHours { get; set; }

    public decimal OvertimeMultiplier { get; set; } = 1m;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>إن أُدرجت في كشف رواتب، نحفظ المرجع لمنع التكرار.</summary>
    public int? PayrollItemId { get; set; }
    public PayrollItem? PayrollItem { get; set; }
}
