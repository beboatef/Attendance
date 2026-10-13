using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>
/// تصحيح يدوي لسجل حضور: يحفظ القيمة الأصلية والمصححة والسبب والمستخدم والوقت.
/// البصمات الخام تبقى محفوظة دائمًا ولا تُعدَّل.
/// </summary>
public class AttendanceCorrection : BaseEntity
{
    public int DailyAttendanceId { get; set; }
    public DailyAttendance? DailyAttendance { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime WorkDate { get; set; }

    /// <summary>اسم الحقل المصحح (CheckInTime / CheckOutTime / Status / الأوقات ... إلخ).</summary>
    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public string Reason { get; set; } = string.Empty;

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Approved;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
}
