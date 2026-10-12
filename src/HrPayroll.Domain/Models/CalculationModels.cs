using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Models;

/// <summary>سياسة/إعدادات الحساب (قابلة للتعديل من الإعدادات).</summary>
public sealed class AttendancePolicy
{
    /// <summary>عدد أيام العمل في الشهر (افتراضي 30).</summary>
    public int WorkDaysPerMonth { get; init; } = 30;

    /// <summary>عدد ساعات العمل في اليوم (افتراضي 8).</summary>
    public int WorkHoursPerDay { get; init; } = 8;

    /// <summary>معامل الإضافي الافتراضي.</summary>
    public decimal DefaultOvertimeMultiplier { get; init; } = 1m;

    /// <summary>هل يُحتسب خصم التأخير من الراتب؟ (لا يُفترض دون تفعيل صريح)</summary>
    public bool LateDeductionEnabled { get; init; } = true;

    /// <summary>هل يُحتسب خصم الغياب من الراتب؟ (معطّل افتراضيًا)</summary>
    public bool AbsenceDeductionEnabled { get; init; }

    /// <summary>دقائق قبل بداية الوردية تُعتبر بصمة دخول مقبولة.</summary>
    public int EarlyArrivalToleranceMinutes { get; init; } = 120;

    /// <summary>دقائق بعد نهاية الوردية تُعتبر بصمة خروج مقبولة (سقف الإضافي).</summary>
    public int OvertimeToleranceMinutes { get; init; } = 240;

    /// <summary>نسبة الطول التي يُقبل خلالها اعتبار أول بصمة "دخول" (من بداية الوردية).</summary>
    public double EntryZoneRatio { get; init; } = 0.5;

    /// <summary>أقل دقائق إضافي معتبرة.</summary>
    public int MinimumOvertimeMinutes { get; init; } = 1;

    public decimal HourlyRate(decimal basicSalary) =>
        Math.Round(basicSalary / (WorkDaysPerMonth * WorkHoursPerDay), 6, MidpointRounding.AwayFromZero);

    public decimal MinuteRate(decimal basicSalary) =>
        Math.Round(basicSalary / (WorkDaysPerMonth * WorkHoursPerDay * 60m), 8, MidpointRounding.AwayFromZero);
}

/// <summary>بصمة خام مدخلة للمصنّف.</summary>
public sealed record RawPunchInput(
    int Id,
    string DeviceEmployeeCode,
    string? DeviceId,
    string? AttType,
    DateTime PunchTime,
    string? RawTimeText,
    int? RowNumber);

/// <summary>حدث مصنف.</summary>
public sealed record ClassifiedEvent(
    RawPunchInput Punch,
    AttendanceEventType EventType,
    ClassificationStatus Status,
    ClassificationSource Source,
    decimal Confidence,
    string? Note);

/// <summary>نتيجة تصنيف يوم كامل.</summary>
public sealed class DayClassificationResult
{
    public DateTime WorkDate { get; init; }
    public List<ClassifiedEvent> Events { get; init; } = new();
    public DateTime? CheckIn { get; init; }
    public DateTime? CheckOut { get; init; }
    public int? EntryPunchId { get; init; }
    public int? ExitPunchId { get; init; }
    public ClassificationStatus Status { get; init; }
    public AttendanceStatus SuggestedStatus { get; init; }
    public bool HasIncompletePunches { get; init; }
    public string? Reason { get; init; }
    public int LateMinutes { get; init; }
    public int EarlyLeaveMinutes { get; init; }
    public int CalculatedOvertimeMinutes { get; init; }
}

/// <summary>نتيجة حساب راتب موظف.</summary>
public sealed record PayrollComputation(
    decimal BasicSalary,
    int LateMinutes,
    decimal MinuteRate,
    decimal LateDeduction,
    decimal ApprovedOvertimeHours,
    decimal OvertimeMultiplier,
    decimal OvertimeValue,
    decimal LoansTotal,
    decimal DeductionsTotal,
    decimal OtherDeductions,
    decimal GrossSalary,
    decimal NetSalary);
