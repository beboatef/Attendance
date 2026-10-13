using System.Text.Json;
using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Models;

/// <summary>
/// سياسة/إعدادات الحساب (قابلة للتعديل من الإعدادات).
/// كل الافتراضات هنا هي افتراضات سياسة الشركة القابلة للتغيير وليست قواعد قانونية عامة.
/// </summary>
public sealed class AttendancePolicy
{
    /// <summary>عدد أيام الشهر المستخدم لحساب الأجر اليومي (افتراضي 30).</summary>
    public int WorkDaysPerMonth { get; init; } = 30;

    /// <summary>عدد ساعات العمل في اليوم (افتراضي 8).</summary>
    public int WorkHoursPerDay { get; init; } = 8;

    /// <summary>معامل الإضافي الافتراضي. سياسة الشركة = 1 (بدون أي مضاعفة).</summary>
    public decimal DefaultOvertimeMultiplier { get; init; } = 1m;

    /// <summary>هل يُحتسب خصم التأخير من الراتب؟</summary>
    public bool LateDeductionEnabled { get; init; } = true;

    /// <summary>هل يُحتسب خصم الغياب من الراتب؟</summary>
    public bool AbsenceDeductionEnabled { get; init; } = true;

    /// <summary>هل يُحتسب خصم الإجازة بدون أجر؟</summary>
    public bool UnpaidLeaveDeductionEnabled { get; init; } = true;

    /// <summary>دقائق السماح عند الدخول الافتراضية إذا لم تُضبط في الوردية.</summary>
    public int EntryGraceMinutes { get; init; } = 30;

    /// <summary>دقائق السماح عند الخروج الافتراضية إذا لم تُضبط في الوردية.</summary>
    public int ExitGraceMinutes { get; init; } = 30;

    /// <summary>خطوة تقريب الإضافي النهائي بالدقائق (1 = بلا تقريب، 5/15/30 ...).</summary>
    public int OvertimeRoundingStepMinutes { get; init; } = 1;

    /// <summary>هل يُقرَّب الإضافي للأسفل؟</summary>
    public bool RoundOvertimeDown { get; init; } = true;

    /// <summary>دقائق قبل بداية الوردية تُعتبر بصمة دخول مقبولة.</summary>
    public int EarlyArrivalToleranceMinutes { get; init; } = 120;

    /// <summary>دقائق بعد نهاية الوردية تُعتبر بصمة خروج مقبولة (سقف الإضافي).</summary>
    public int OvertimeToleranceMinutes { get; init; } = 240;

    /// <summary>نسبة الطول التي يُقبل خلالها اعتبار أول بصمة "دخول" (من بداية الوردية).</summary>
    public double EntryZoneRatio { get; init; } = 0.5;

    /// <summary>أقل دقائق إضافي معتبرة.</summary>
    public int MinimumOvertimeMinutes { get; init; } = 1;

    /// <summary>هل الراتب الشهري يشمل بالفعل أيام الراحة المدفوعة؟ (سياسة الشركة: نعم)</summary>
    public bool MonthlySalaryIncludesPaidRestDays { get; init; } = true;

    /// <summary>الأجر اليومي = الراتب الأساسي / أيام الشهر، إلا إذا كان للموظف أجر يومي مستقل.</summary>
    public decimal DailyRateFor(decimal basicSalary, decimal? dailyRateOverride = null)
    {
        if (dailyRateOverride is > 0m) return Math.Round(dailyRateOverride.Value, 6, MidpointRounding.AwayFromZero);
        if (WorkDaysPerMonth <= 0) return 0m;
        return Math.Round(basicSalary / WorkDaysPerMonth, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>الأجر بالساعة = الأجر اليومي / ساعات العمل اليومية.</summary>
    public decimal HourlyRateFor(decimal dailyRate)
        => WorkHoursPerDay <= 0 ? 0m : Math.Round(dailyRate / WorkHoursPerDay, 6, MidpointRounding.AwayFromZero);

    public decimal MinuteRateFor(decimal dailyRate)
        => Math.Round(HourlyRateFor(dailyRate) / 60m, 8, MidpointRounding.AwayFromZero);

    public decimal HourlyRateOfSalary(decimal basicSalary, decimal? dailyRateOverride = null)
        => HourlyRateFor(DailyRateFor(basicSalary, dailyRateOverride));

    /// <summary>يقرب دقائق الإضافي النهائية مرة واحدة وفق الخطوة المُعدّة.</summary>
    public int RoundOvertimeMinutes(int minutes)
    {
        if (minutes <= 0) return 0;
        if (OvertimeRoundingStepMinutes <= 1) return minutes;
        var step = OvertimeRoundingStepMinutes;
        var steps = minutes / (decimal)step;
        var rounded = RoundOvertimeDown
            ? Math.Floor(steps)
            : Math.Round(steps, MidpointRounding.AwayFromZero);
        return (int)rounded * step;
    }
}

/// <summary>سياسة الإضافي المطبقة على موظف في يوم/فترة.</summary>
public sealed record OvertimePolicy(decimal Multiplier, int RoundingStepMinutes, bool RoundDown)
{
    public static OvertimePolicy Company(AttendancePolicy policy) =>
        new(policy.DefaultOvertimeMultiplier <= 0 ? 1m : policy.DefaultOvertimeMultiplier,
            policy.OvertimeRoundingStepMinutes, policy.RoundOvertimeDown);
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

/// <summary>مدخلات حساب راتب موظف واحد في شهر.</summary>
public sealed class PayrollInputs
{
    public decimal BasicSalary { get; init; }
    public decimal? DailyRateOverride { get; init; }
    public decimal StandardWorkHours { get; init; } = 8m;

    public int LateMinutes { get; init; }
    public int RawOvertimeMinutes { get; init; }
    public int ApprovedOvertimeMinutes { get; init; }
    public int PaidOvertimeMinutes { get; init; }

    public int AttendanceDays { get; init; }
    public int PaidRestDays { get; init; }
    public decimal PaidLeaveDays { get; init; }
    public decimal UnpaidLeaveDays { get; init; }
    public decimal AbsenceDays { get; init; }

    public decimal Advances { get; init; }

    /// <summary>خصومات مُسجّلة ومعتمدة (جدول الخصومات).</summary>
    public decimal DeductionsTotal { get; init; }

    public decimal OtherDeductions { get; init; }
    public decimal Bonuses { get; init; }
    public decimal Allowances { get; init; }
    public decimal AdditionalDays { get; init; }
    public decimal? AdditionalDaysRate { get; init; }

    public decimal OvertimeMultiplier { get; init; } = 1m;

    /// <summary>أيام الاحتساب كأسماء للتفاصيل.</summary>
    public string? Notes { get; init; }
}

/// <summary>نتيجة حساب راتب موظف: كل مكوّن مفصول لوحده لأغراض المراجعة والتدقيق.</summary>
public sealed record PayrollComputation(
    decimal BasicSalary,
    decimal DailyRate,
    decimal HourlyRate,
    decimal MinuteRate,
    int DailyRateBasisDays,
    int WorkHoursPerDay,
    int AttendanceDays,
    int PaidRestDays,
    decimal PaidLeaveDays,
    decimal UnpaidLeaveDays,
    decimal AbsenceDays,
    int LateMinutes,
    decimal LateDeduction,
    int RawOvertimeMinutes,
    decimal RawOvertimeHours,
    decimal ApprovedOvertimeHours,
    decimal PaidOvertimeHours,
    decimal OvertimeMultiplier,
    decimal OvertimeAmount,
    decimal AbsenceDeduction,
    decimal UnpaidLeaveDeduction,
    decimal Advances,
    decimal DeductionsTotal,
    decimal OtherDeductions,
    decimal Bonuses,
    decimal Allowances,
    decimal AdditionalDaysCount,
    decimal AdditionalDaysAmount,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal NetSalary,
    string CalculationDetails)
{
    /// <summary>الإجمالي قبل أي خصم (يحفظ للتوافق مع الحقل القديم).</summary>
    public decimal GrossSalary => GrossEarnings;

    public static string SerializeDetails(object details) =>
        JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = false });
}
