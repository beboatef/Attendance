using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>ملخص الحضور النهائي لموظف في يوم/وردية.</summary>
public class DailyAttendance : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>تاريخ العمل (تاريخ بداية الوردية).</summary>
    public DateTime WorkDate { get; set; }

    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    /// <summary>كيف حُدِّدت الوردية لهذا اليوم.</summary>
    public ShiftDetectionSource DetectionSource { get; set; } = ShiftDetectionSource.None;

    /// <summary>سبب الحاجة للمراجعة عند غموض الوردية أو البصمات.</summary>
    public string? NeedsReviewReason { get; set; }

    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }

    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }

    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }

    /// <summary>الإضافي الخام المحسوب آليًا بالدقائق (قبل أي اعتماد).</summary>
    public int RawOvertimeMinutes { get; set; }

    /// <summary>الإضافي المعتمد يدويًا بالدقائق (المسموح بدفعه).</summary>
    public int ApprovedOvertimeMinutes { get; set; }

    /// <summary>الإضافي المدفوع فعليًا بالدقائق (بعد التقريب وقواعد السياسة).</summary>
    public int PaidOvertimeMinutes { get; set; }

    public AttendanceStatus Status { get; set; }
    public ClassificationStatus PunchReviewStatus { get; set; }

    public bool HasIncompletePunches { get; set; }
    public bool IsManualOverride { get; set; }

    /// <summary>يوم راحة أسبوعية.</summary>
    public bool IsWeeklyRestDay { get; set; }

    /// <summary>عطلة رسمية.</summary>
    public bool IsHoliday { get; set; }

    public int? HolidayId { get; set; }
    public PublicHoliday? Holiday { get; set; }

    /// <summary>الإجازة المعتمدة التي غطّت هذا اليوم (إن وُجدت).</summary>
    public int? LeaveRequestId { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }

    public int? EntryEventId { get; set; }
    public int? ExitEventId { get; set; }

    public string? Notes { get; set; }
    public string? AdminNotes { get; set; }

    /// <summary>إجمالي دقائق العمل الفعلية (خروج − دخول) عند توفر البصمتين.</summary>
    public int WorkedMinutes =>
        CheckInTime.HasValue && CheckOutTime.HasValue
            ? Math.Max(0, (int)Math.Round((CheckOutTime.Value - CheckInTime.Value).TotalMinutes))
            : 0;

    public ICollection<AttendanceCorrection> Corrections { get; set; } = new List<AttendanceCorrection>();
}
