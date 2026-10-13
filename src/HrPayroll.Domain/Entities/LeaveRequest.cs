using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>طلب إجازة أو تصنيف يوم غياب كإجازة معتمدة.</summary>
public class LeaveRequest : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public LeaveType LeaveType { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? Reason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>هل هذه إجازة مدفوعة؟</summary>
    public bool IsPaid { get; set; } = true;

    /// <summary>عدد الأيام التقويمية للطلب.</summary>
    public int DaysCount { get; set; }

    /// <summary>عدد أيام العمل الفعلية (بدون أيام الراحة الأسبوعية والعطلات).</summary>
    public decimal WorkingDays { get; set; }

    /// <summary>مصدر الطلب: يدوي أم تحويل غياب.</summary>
    public bool CreatedFromAbsence { get; set; }

    /// <summary>كشف الرواتب الذي استُخدمت فيه هذه الإجازة (لمنع الخصم المزدوج).</summary>
    public int? PayrollItemId { get; set; }
    public PayrollItem? PayrollItem { get; set; }
}
