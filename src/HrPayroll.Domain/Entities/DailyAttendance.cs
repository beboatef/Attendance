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

    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }

    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }

    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }

    /// <summary>الإضافي المحسوب آليًا بالدقائق.</summary>
    public int CalculatedOvertimeMinutes { get; set; }

    /// <summary>الإضافي المعتمد بالدقائق (بعد الاعتماد اليدوي إن وجد).</summary>
    public int ApprovedOvertimeMinutes { get; set; }

    public AttendanceStatus Status { get; set; }
    public ClassificationStatus PunchReviewStatus { get; set; }

    public bool HasIncompletePunches { get; set; }
    public bool IsManualOverride { get; set; }

    public int? EntryEventId { get; set; }
    public int? ExitEventId { get; set; }

    public string? Notes { get; set; }
    public string? AdminNotes { get; set; }
}
