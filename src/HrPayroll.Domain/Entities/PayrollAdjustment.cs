using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>
/// تسوية مالية لموظف في شهر محدد: مكافأة، بدل، خصم آخر، أو أيام عمل إضافية مستحقة.
/// كل تسوية تُستخدم مرة واحدة فقط (PayrollItemId يمنع الخصم/الإضافة المزدوجة).
/// </summary>
public class PayrollAdjustment : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int Month { get; set; }
    public int Year { get; set; }

    public AdjustmentType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>المبلغ للأنواع المالية (مكافأة/بدل/خصم آخر).</summary>
    public decimal Amount { get; set; }

    /// <summary>عدد الأيام لنوع "أيام إضافية".</summary>
    public decimal? Days { get; set; }

    /// <summary>سعر اليوم المستخدم للأيام الإضافية (افتراضيًا: الأجر اليومي للموظف).</summary>
    public decimal? DailyRateOverride { get; set; }

    /// <summary>سبب مستقل عن الشهر (اختياري) للأيام الإضافية.</summary>
    public DateTime? ReferenceDate { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>كشف الرواتب الذي استُخدمت فيه هذه التسوية (لمنع التكرار).</summary>
    public int? PayrollItemId { get; set; }
    public PayrollItem? PayrollItem { get; set; }
}
