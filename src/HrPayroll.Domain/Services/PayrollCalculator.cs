using HrPayroll.Domain.Models;

namespace HrPayroll.Domain.Services;

/// <summary>حساب قيم الرواتب. منطق نقي قابل للاختبار.</summary>
public static class PayrollCalculator
{
    /// <summary>تقريب القيم المالية إلى منزلتين عشريتين.</summary>
    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// يحسب كشف راتب موظف واحد.
    /// صافي الراتب = الراتب الأساسي + قيمة الإضافي − خصم التأخير − السلف − الخصومات − أي خصومات أخرى معتمدة.
    /// </summary>
    public static PayrollComputation Compute(
        decimal basicSalary,
        int lateMinutes,
        decimal approvedOvertimeHours,
        decimal overtimeMultiplier,
        decimal loansTotal,
        decimal deductionsTotal,
        decimal otherDeductions,
        AttendancePolicy policy)
    {
        if (basicSalary < 0) throw new ArgumentOutOfRangeException(nameof(basicSalary));
        if (overtimeMultiplier <= 0) overtimeMultiplier = policy.DefaultOvertimeMultiplier;

        var minuteRate = policy.MinuteRate(basicSalary);
        var hourlyRate = policy.HourlyRate(basicSalary);

        var lateDeduction = policy.LateDeductionEnabled
            ? Round2(Math.Max(0, lateMinutes) * minuteRate)
            : 0m;

        var overtimeValue = Round2(Math.Max(0m, approvedOvertimeHours) * hourlyRate * overtimeMultiplier);

        var gross = Round2(basicSalary + overtimeValue);
        var net = Round2(gross - lateDeduction - loansTotal - deductionsTotal - otherDeductions);
        if (net < 0) net = 0m; // لا نسمح بصافي سالب

        return new PayrollComputation(
            basicSalary,
            Math.Max(0, lateMinutes),
            minuteRate,
            lateDeduction,
            approvedOvertimeHours,
            overtimeMultiplier,
            overtimeValue,
            Round2(loansTotal),
            Round2(deductionsTotal),
            Round2(otherDeductions),
            gross,
            net);
    }

    /// <summary>يحسب قيمة الإضافي وحدها حسب معامل الوردية/الإعداد.</summary>
    public static decimal CalculateOvertimeValue(decimal basicSalary, decimal approvedHours, decimal multiplier, AttendancePolicy policy)
        => Round2(Math.Max(0m, approvedHours) * policy.HourlyRate(basicSalary) * (multiplier <= 0 ? policy.DefaultOvertimeMultiplier : multiplier));
}
